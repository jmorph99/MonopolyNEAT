using System;

namespace MONOPOLY
{
    // RecordingPolicy is the IPolicy used during PPO rollouts. It does
    // three things at every decision:
    //
    //   1. Propagate the actor MLP on the projected board state.
    //   2. For the five binary decisions (buy, mortgage, advance, offer
    //      trade, accept trade), SAMPLE the action stochastically from
    //      Bernoulli(Y[dim]) rather than thresholding deterministically.
    //      This is what gives PPO a useful policy gradient — without
    //      exploration the ratio in the clipped objective stays at 1.
    //   3. Snapshot the trajectory entry (state, action_dim, action,
    //      action_prob, critic value, placeholder reward).
    //
    // Decisions whose output isn't in {0,1} — jail (3-way), auction bid
    // (continuous money), house build / sell (continuous count) — are
    // taken deterministically using the same thresholds NeuralPolicy uses
    // at eval time, but NOT recorded as learnable. PPO learns the binary
    // dimensions only on this first pass.
    //
    // Lifecycle: one RecordingPolicy per seat per game. After Arena.PlayOne
    // returns, the PpoTrainer calls Finalise(terminalReward) to attach the
    // game outcome to the last entry of the trajectory and then collects
    // .trajectory into the rollout batch.
    public class RecordingPolicy : IPolicy
    {
        public TRAINING.MLP actor;
        public TRAINING.MLP critic;
        public TRAINING.Trajectory trajectory;

        public RecordingPolicy(TRAINING.MLP actor, TRAINING.MLP critic)
        {
            this.actor = actor;
            this.critic = critic;
            this.trajectory = new TRAINING.Trajectory();
        }

        // Convenience: project, propagate actor, sample binary action,
        // record trajectory entry, return action as a 0/1 boolean.
        private bool SampleAndRecord(Board board, int playerIdx, int actionDim, DecisionContext ctx)
        {
            float[] X = Projection.Project(board, playerIdx, ctx);
            float[] Y = actor.Propagate(X);
            float p = Y[actionDim];
            // Numerical safety: clamp slightly away from {0,1} so the
            // gradient backend's log(p) and log(1-p) don't blow up.
            if (p < 1e-6f) p = 1e-6f;
            else if (p > 1.0f - 1e-6f) p = 1.0f - 1e-6f;

            float u = (float)RNG.instance.gen.NextDouble();
            int action = u < p ? 1 : 0;

            float v = critic.Propagate(X)[0];

            trajectory.observations.Add(X);
            trajectory.actionDims.Add(actionDim);
            trajectory.actions.Add(action);
            trajectory.probs.Add(p);
            trajectory.values.Add(v);
            trajectory.rewards.Add(0.0f);

            return action == 1;
        }

        public Player.EBuyDecision DecideBuy(Board board, int playerIdx, int tileIdx)
        {
            bool yes = SampleAndRecord(board, playerIdx, 0, DecisionContext.Tile(tileIdx));
            return yes ? Player.EBuyDecision.BUY : Player.EBuyDecision.AUCTION;
        }

        public Player.EDecision DecideMortgage(Board board, int playerIdx, int tileIdx)
        {
            return SampleAndRecord(board, playerIdx, 2, DecisionContext.Tile(tileIdx))
                ? Player.EDecision.YES : Player.EDecision.NO;
        }

        public Player.EDecision DecideAdvance(Board board, int playerIdx, int tileIdx)
        {
            return SampleAndRecord(board, playerIdx, 3, DecisionContext.Tile(tileIdx))
                ? Player.EDecision.YES : Player.EDecision.NO;
        }

        public Player.EDecision DecideOfferTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            int[] flagged = Concat(giving, receiving);
            return SampleAndRecord(board, playerIdx, 7, DecisionContext.Trade(flagged, moneyBalance))
                ? Player.EDecision.YES : Player.EDecision.NO;
        }

        public Player.EDecision DecideAcceptTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            int[] flagged = Concat(giving, receiving);
            return SampleAndRecord(board, playerIdx, 8, DecisionContext.Trade(flagged, moneyBalance))
                ? Player.EDecision.YES : Player.EDecision.NO;
        }

        // Non-learnable: same deterministic thresholds NeuralPolicy uses.
        // Propagated through the actor but not recorded as a trajectory entry.
        public Player.EJailDecision DecideJail(Board board, int playerIdx)
        {
            float[] X = Projection.Project(board, playerIdx);
            float[] Y = actor.Propagate(X);
            if (Y[1] < 0.333f) return Player.EJailDecision.CARD;
            if (Y[1] < 0.666f) return Player.EJailDecision.ROLL;
            return Player.EJailDecision.PAY;
        }

        public int DecideAuctionBid(Board board, int playerIdx, int tileIdx)
        {
            float[] X = Projection.Project(board, playerIdx, DecisionContext.Tile(tileIdx));
            float[] Y = actor.Propagate(X);
            float money = Projection.ConvertMoneyValue(Y[4]);
            Monopoly.Analytics.instance.MakeBid(tileIdx, (int)money);
            return (int)money;
        }

        public int DecideBuildHouse(Board board, int playerIdx, int set)
        {
            int firstInSet = Board.SETS[set, 0];
            float[] X = Projection.Project(board, playerIdx, DecisionContext.Tile(firstInSet));
            float[] Y = actor.Propagate(X);
            return (int)Projection.ConvertHouseValue(Y[5]);
        }

        public int DecideSellHouse(Board board, int playerIdx, int set)
        {
            int firstInSet = Board.SETS[set, 0];
            float[] X = Projection.Project(board, playerIdx, DecisionContext.Tile(firstInSet));
            float[] Y = actor.Propagate(X);
            return (int)Projection.ConvertHouseValue(Y[6]);
        }

        public TRAINING.Trajectory Finalise(float terminalReward)
        {
            if (trajectory.rewards.Count > 0)
            {
                trajectory.rewards[trajectory.rewards.Count - 1] = terminalReward;
            }
            return trajectory;
        }

        private static int[] Concat(int[] a, int[] b)
        {
            int[] r = new int[a.Length + b.Length];
            Array.Copy(a, 0, r, 0, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
