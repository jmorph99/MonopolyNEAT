using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Monopoly
{
    public class Tournament
    {
        public static int TOURNAMENT_SIZE = 256;
        public static int ROUND_SIZE = 2000; //2000
        
        public static int WORKERS = 20; //20
        public static int BATCH_SIZE = 20; //20

        public NEAT.Genotype champion = null;
        public float championScore = 0.0f;

        public List<NEAT.Phenotype> contestants;
        public List<NEAT.Genotype> contestants_g;

        public Tournament()
        {
            contestants = new List<NEAT.Phenotype>();
            contestants_g = new List<NEAT.Genotype>();
        }

        public void Initialise()
        {
            int INPUTS = 126;
            int OUTPUTS = 9;

            NEAT.Population.instance.GenerateBasePopulation(TOURNAMENT_SIZE, INPUTS, OUTPUTS);
        }

        public void ExecuteTournament()
        {
            Console.WriteLine("TOURNAMENT #" + NEAT.Population.instance.GENERATION);

            contestants.Clear();
            contestants_g.Clear();

            for (int i = 0; i < TOURNAMENT_SIZE; i++)
            {
                NEAT.Population.instance.genetics[i].bracket = 0;
                NEAT.Population.instance.population[i].score = 0.0f;
                
                contestants.Add(NEAT.Population.instance.population[i]);
                contestants_g.Add(NEAT.Population.instance.genetics[i]);
            }

            while (contestants.Count > 1)
            {
                ExecuteTournamentRound();
            }

            for (int i = 0; i < TOURNAMENT_SIZE; i++)
            {
                float top = 0.0f;

                if (champion != null)
                {
                    top = champion.bracket;
                }

                float diff = NEAT.Population.instance.genetics[i].bracket - top;
                NEAT.Population.instance.genetics[i].fitness = championScore + diff * 5;
            }

            champion = contestants_g[0];
            championScore = contestants_g[0].fitness;
        }

        public void ExecuteTournamentRound()
        {

            Console.WriteLine("ROUND SIZE " + contestants.Count.ToString());

            List<NEAT.Phenotype> cs = new List<NEAT.Phenotype>();
            List<NEAT.Genotype> cs_g = new List<NEAT.Genotype>();

            RNG.instance.DoubleShuffle(contestants, contestants_g, ref cs, ref cs_g);

            for (int i = 0; i < TOURNAMENT_SIZE; i++)
            {
                NEAT.Population.instance.population[i].score = 0.0f;
            }

            contestants = cs;
            contestants_g = cs_g;

            for (int i = 0; i < contestants.Count; i += 4)
            {
                int played = 0;

                Console.WriteLine("BRACKET (" + ( i / 4 ) + ")");

                while (played < ROUND_SIZE)
                {
                    Console.WriteLine("Initialised Workers");

                    Thread[] workers = new Thread[WORKERS];

                    for (int t = 0; t < WORKERS; t++)
                    {
                        workers[t] = new Thread(() => PlayGameThread(this, i));
                        workers[t].Start();
                    }

                    for (int t = 0; t < WORKERS; t++)
                    {
                        workers[t].Join();
                    }

                    played += WORKERS * BATCH_SIZE;

                    for (int c = 0; c < 40; c++)
                    {
                        Console.WriteLine("index: " + c.ToString() + ", " + String.Format("{0:0.000}", Monopoly.Analytics.instance.ratio[c]));
                    }
                }

                int mi = 0;
                float ms = contestants[i].score;

                for (int j = 1; j < 4; j++)
                {
                    if (ms < contestants[i + j].score)
                    {
                        mi = j;
                        ms = contestants[i + j].score;
                    }
                }

                for (int j = 0; j < 4; j++)
                {
                    if (j == mi)
                    {
                        contestants_g[i + j].bracket++;
                        continue;
                    }

                    contestants[i + j] = null;
                }
            }

            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i] == null)
                {
                    contestants.RemoveAt(i);
                    contestants_g.RemoveAt(i);
                    i--;
                }
            }

            return;
        }

        public static void PlayGameThread(Tournament instance, int i)
        {
            for (int game = 0; game < BATCH_SIZE; game++)
            {
                NetworkAdapter adapter = new NetworkAdapter();

                NEAT.Phenotype[] networks = new NEAT.Phenotype[]
                {
                    instance.contestants[i],
                    instance.contestants[i + 1],
                    instance.contestants[i + 2],
                    instance.contestants[i + 3],
                };

                networks = RNG.instance.Shuffle(networks);

                MONOPOLY.IPolicy[] policies = new MONOPOLY.IPolicy[4];
                for (int p = 0; p < 4; p++)
                {
                    policies[p] = new MONOPOLY.NeuralPolicy(networks[p], adapter);
                }

                MONOPOLY.Board board = new MONOPOLY.Board(adapter, policies);

                MONOPOLY.Board.EOutcome outcome = MONOPOLY.Board.EOutcome.ONGOING;

                while (outcome == MONOPOLY.Board.EOutcome.ONGOING)
                {
                    outcome = board.Step();
                }

                int winnerSeat = -1;
                switch (outcome)
                {
                    case MONOPOLY.Board.EOutcome.WIN1: winnerSeat = 0; break;
                    case MONOPOLY.Board.EOutcome.WIN2: winnerSeat = 1; break;
                    case MONOPOLY.Board.EOutcome.WIN3: winnerSeat = 2; break;
                    case MONOPOLY.Board.EOutcome.WIN4: winnerSeat = 3; break;
                }

                if (winnerSeat >= 0)
                {
                    NEAT.Phenotype winner = networks[winnerSeat];
                    lock (winner)
                    {
                        winner.score += 1.0f;
                    }

                    MONOPOLY.Player winnerPlayer = board.players[winnerSeat];
                    for (int b = 0; b < winnerPlayer.items.Count; b++)
                    {
                        lock (Monopoly.Analytics.instance.wins)
                        {
                            Monopoly.Analytics.instance.MarkWin(winnerPlayer.items[b]);
                        }
                    }
                }
                else if (outcome == MONOPOLY.Board.EOutcome.DRAW)
                {
                    lock (networks)
                    {
                        networks[0].score += 0.25f;
                        networks[1].score += 0.25f;
                        networks[2].score += 0.25f;
                        networks[3].score += 0.25f;
                    }
                }
            }
        }
    }
}
