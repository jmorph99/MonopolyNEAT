# ML strategies for the Monopoly simulator

Target hardware: ~5-year-old consumer desktop, RTX 2060 (6 GB VRAM, ~6.5 TFLOPS FP32), 6–8 CPU cores, 16–32 GB RAM.

Constraint: the simulator is .NET 8. For any gradient-based method, the realistic path is **ONNX Runtime + TorchSharp** in-process, or a Python trainer (PyTorch) that talks to the C# simulator over a socket / shared memory. The simulator itself stays as-is.

All five proposals below assume the input/output contract stays at 126 → 9 (or a refactored version) and one decision = one network forward pass. The existing self-play harness (`Tournament.cs`) is the data-generation engine for every method here.

---

## 0. Current strategy — NEAT (baseline)

**What:** Neuroevolution of Augmenting Topologies. Population of 256 genomes (vertices + edges with innovation numbers); fitness is bracket depth in a single-elimination tournament of 2000-game brackets; speciation by edge-history distance; mutation adds nodes / edges / perturbs weights; crossover aligns by innovation number. No gradients, no replay, no value function. See `Monopoly/NeuroEvolution/`.

**Strengths:** Topology evolves alongside weights, so it discovers its own architecture. Embarrassingly parallel — already saturates 20 CPU threads. Doesn't need a GPU at all.

**Known weaknesses for this environment:**
- Pure self-play in a non-transitive 4-player game cycles (rock-paper-scissors strategies).
- Win/loss is the only signal — extremely sparse over ~300-turn games, so credit assignment to any one decision is very weak.
- 10-iteration relaxation `Propagate` makes the network's effective depth opaque; not obvious it's exploiting the recurrent allowance.

**Hardware:** Trivial. CPU-only. Already proven on this machine.

---

## 1. Evolution Strategies (CMA-ES or OpenAI-ES)

**What:** Fix the network topology up front (e.g. 126 → 256 → 256 → 9, ~100K params). Each generation, sample N parameter vectors as Gaussian perturbations of the current mean, evaluate by self-play fitness, take a fitness-weighted average step toward the better samples. CMA-ES additionally adapts the sampling covariance; OpenAI-ES uses isotropic noise and scales better.

**Why it fits:**
- Drop-in slot where NEAT currently lives — same fitness-from-tournament loop, just a different update rule.
- No gradients means the C# simulator never needs to expose intermediate state; just evaluate and return win-rate.
- Smoother / faster convergence than NEAT for a fixed-shape problem (which Monopoly arguably is, once you stop trusting NEAT to discover useful topology).

**Hardware fit:** CPU-only, trivially fits. CMA-ES with a 100K-param net is feasible (covariance is O(n²) — borderline; OpenAI-ES uses no covariance and handles millions of params no problem). RTX 2060 unused.

**Adaptation:** Replace `Population.NewGeneration` with a parameter-vector resample. Keep `Tournament.ExecuteTournament` exactly as-is for fitness. Smallest engineering lift of the five.

**Tradeoff:** Loses topology-evolution. If the current NEAT champion's topology is actually doing something clever, ES on a fixed shape might underperform until you find a comparable architecture.

---

## 2. PPO with multi-agent self-play

**What:** Actor-critic policy gradient. The 9 decision heads become categorical action distributions (with a Beta head for the continuous auction-bid magnitude). A value head predicts expected return. Each game emits a trajectory of (state, action, log-prob, value, reward). Generalized Advantage Estimation propagates the terminal win/loss back through the trajectory. Clipped policy-gradient updates.

**Why it fits:**
- The standard modern baseline for game RL. Well-documented failure modes, lots of reference code.
- The existing threaded game-batching maps directly onto PPO's parallel-actor architecture.
- Can mix in reward shaping (per-turn net-worth delta, property-set acquisition bonus) to densify the signal — NEAT's fitness function can't do that cleanly.

**Hardware fit:** Network is small (~100K params). One forward pass per decision. Trajectory storage for 256 games × 300 turns × 9 decisions is well under a GB. GPU forward-passes for 256 parallel games batch easily into the 2060's 6 GB. CPU still does the game logic.

**Adaptation:** Add an RPC layer (named pipe or gRPC) from `NeuralPolicy` to a Python PPO trainer; or use TorchSharp in-process. Define a per-turn reward (probably net worth delta) on top of the terminal win indicator. PPO hyperparameters (clip 0.2, lambda 0.95, 4–10 epochs per rollout) port directly.

**Tradeoff:** Multi-agent non-stationarity — your opponents are also learning, so the gradient is noisier than single-agent PPO. Sparse reward is mitigated but not solved by shaping; long-horizon credit assignment remains hard.

---

## 3. Rainbow DQN (multi-head Q-values)

**What:** Off-policy value learning. Each decision head is a separate Q-function over its action set. Replay buffer of (state, action, reward, next_state) tuples. Target network for stability. Rainbow's extras: double Q-learning, dueling networks, distributional Bellman (C51), n-step returns, prioritized replay, noisy nets for exploration.

**Why it fits:**
- 7 of the 9 decision heads are binary or small-integer (buy / jail / mortgage / advance / build / sell / accept-trade) — Q-learning's native domain.
- Off-policy means old self-play data stays useful indefinitely — unlike NEAT, which throws away every previous generation. The replay buffer becomes a permanent corpus of game experience.
- Distributional Q-learning (C51) is particularly suited to Monopoly's heavy-tailed outcomes (one bad rent payment can end the game).

**Hardware fit:** Replay buffer of 1M transitions × ~600 bytes/state ≈ 600 MB — fits in RAM. Network is small enough that 2060 handles 256-sample minibatches in single-digit ms. Training and self-play can run concurrently (DQN's defining advantage).

**Adaptation:** Split the auction-bid head off into a separate continuous-action algorithm (DDPG or SAC) or discretize bids into ~10 buckets. Otherwise the 8 remaining discrete heads each get their own Q-output, sharing a trunk. Same RPC/in-process choice as PPO.

**Tradeoff:** DQN is notoriously unstable in multi-agent settings — the "next-state value" target is non-stationary when opponents are also updating. Mitigations: train against a frozen opponent pool (related to PSRO below) or use slower target updates.

---

## 4. PSRO / League self-play (PPO as inner learner)

**What:** Maintain a *league* of frozen past policies. Each new training run optimizes a fresh policy against a *mixture* of league members chosen by a meta-game solver (Nash on the empirical win-rate matrix, or fictitious self-play, or prioritized rank). Periodically, the new policy is frozen and added to the league. The inner training algorithm can be anything — PPO is the obvious pick.

**Why it fits:**
- Directly attacks NEAT's biggest failure mode in this game: 4-player non-transitive cycles. Freezing ancestors stops the generation-N+5-loses-to-generation-N drift.
- The 4-player seat slot becomes part of the meta-game — some policies are stronger from one seat than another, and PSRO exposes that.
- Fits naturally on top of any gradient-based method above; this is a training *scheme*, not an algorithm.

**Hardware fit:** A league of 50–100 small policies is ~5–50 MB on disk. Inner PPO has the same footprint as method #2. Meta-game solving is a tiny linear program on a 100×100 win-rate matrix — trivial.

**Adaptation:** Wrap PPO with league management. Store frozen policies as ONNX. Sample opponents per game from the current Nash-mixture distribution. Periodically (every ~50 PPO updates) freeze the current policy if its exploitability against the league dropped below a threshold.

**Tradeoff:** ~5–10× the compute of plain PPO because you run more total games. Engineering lift is the largest of these five — meta-game machinery has to be built, and there are many tunable knobs (oracle threshold, mixing scheme, league pruning).

---

## 5. AlphaZero with chance nodes (expectimax MCTS)

**What:** A policy/value network guides Monte Carlo Tree Search. The standard variant for stochastic games inserts explicit **chance nodes** for dice rolls and card draws — child selection uses UCB on player decisions, weighted expectation on chance outcomes. Self-play games generated by MCTS train the network via cross-entropy on the search-policy and MSE on the search-value.

**Why it fits:**
- The simulator is the perfect ground-truth model — you can `Clone` a `Board` state and roll out from any node. Exactly the setting AlphaZero needs.
- Search makes a weak network strong: even a barely-trained policy plus MCTS plays at a level that pure NEAT will never reach.
- Long horizon stops being a problem — MCTS does the credit assignment that PPO struggles with.

**Hardware fit:** Most expensive of the five. Per-move search budget on a 2060 is realistic at ~100–400 simulations per decision (each one is a network forward pass plus a few clones of `Board`). At 100 sims/move × 9 decisions/turn × 300 turns ≈ 270K forward passes per game — minutes per game vs. NEAT's milliseconds. Plan for hundreds of self-play games per training cycle, not thousands. Training the network itself is cheap (it's small).

**Adaptation:** Make `Board` and `Player` cleanly cloneable (currently they're mostly value types and `List<int>` — straightforward, but verify the RNG is forked properly so simulated branches don't contaminate each other). Implement the MCTS as a separate component sitting between `NeuralPolicy` and the network. Train in PyTorch via RPC.

**Tradeoff:** Wall-clock training time dominates. Worth it only if the other methods plateau; AlphaZero is the highest-ceiling option but the lowest-throughput. The 2060 will be the bottleneck — an RTX 4070+ would change the ROI a lot.

---

## Suggested order of attack

1. **ES** first (1–2 days of work, validates whether NEAT's topology evolution is paying for itself).
2. **PPO** next (1–2 weeks, gets you a modern gradient-based baseline with shaped rewards).
3. **Rainbow DQN** in parallel with PPO if curious (off-policy data reuse is the genuine advantage; ~1 week from a PPO baseline).
4. **PSRO** layered onto PPO once a stable single-policy baseline exists (~1–2 weeks; addresses the non-transitivity problem NEAT can't).
5. **AlphaZero** only if 1–4 plateau and you want a strength ceiling worth the compute cost (~1 month, and expect long training runs).

For all five, the rule-fidelity bugs from `updaterules.md` (especially the `doub`-not-reset bug and the missing monopoly rent doubling) should be fixed first — otherwise you're training networks against a non-Monopoly environment and the results don't transfer to anything else.

---

## Footnote — can a single corpus of self-play games train all six methods?

No. The simulator and the 127-float state encoding are reusable, but the **training targets** each method needs are different, and most methods are **on-policy** — they only learn from games their own current network played.

| Method | Data unit | On-policy? |
|---|---|---|
| NEAT | `(genome_id, win_count)` | Yes — fitness *is* this genome's win rate. |
| ES | `(parameter_vector, fitness)` | Yes — same as NEAT. |
| PPO | `(state, action, log_prob, reward)` | Yes — stale within ~1 policy update. |
| Rainbow DQN | `(state, action, reward, next_state)` | **No** — accepts data from any behavior policy. |
| PSRO | `(state, action, log_prob, reward)` vs league | Inherits PPO's constraint for the learner. |
| AlphaZero | `(state, mcts_visit_distribution, outcome)` | Yes — visit distribution is specific to this network. |

**The lone exception is Rainbow DQN.** Because it's off-policy, it can ingest transitions generated by any other method's self-play. A NEAT generation playing 512K bracket games is a free firehose of DQN training data — DQN can run as a passive consumer of whichever training loop is currently active.

For everything else: pooling doesn't work. Plan on 4–5 separate training pipelines, each producing games used only by itself, optionally plus one DQN process listening to all of them.

Two legitimate ways to share work across methods:

- **Distillation.** If you train one strong reference policy (likely AlphaZero), you can warm-start the others via supervised learning on `(state, reference_action)` pairs. This is teacher-student, not shared training data — but it cuts wall-clock time for the methods that follow.
- **Benchmarking.** Round-robin tournaments mixing methods across the 4 seats produce directly-comparable win-rate matrices. This is the natural use of heterogeneous-per-seat `IPolicy[]`, and it works the same for all six.
