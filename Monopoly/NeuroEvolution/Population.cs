using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEAT
{
    public class Species
    {
        public List<Genotype> members;

        public float topFitness = 0.0f;
        public int staleness = 0;

        public float fitnessSum = 0.0f;

        public Species()
        {
            members = new List<Genotype>();
        }

        public Genotype Breed()
        {
            float roll = (float)RNG.instance.gen.NextDouble();

            if (roll < Crossover.instance.CROSSOVER_CHANCE && members.Count > 1)
            {
                int s1 = RNG.instance.gen.Next(0, members.Count);
                int s2 = RNG.instance.gen.Next(0, members.Count - 1);

                if (s2 >= s1)
                {
                    s2++;
                }

                if (s1 > s2)
                {
                    int temp = s1;
                    s1 = s2;
                    s2 = temp;
                }

                Genotype child = Crossover.instance.ProduceOffspring(members[s1], members[s2]);
                Mutation.instance.MutateAll(child);

                int selection = RNG.instance.gen.Next(0, members.Count);

                return child;
            }
            else
            {
                int selection = RNG.instance.gen.Next(0, members.Count);
                
                Genotype child = members[selection].Clone();
                Mutation.instance.MutateAll(child);

                return child;
            }
        }

        public void SortMembers()
        {
            members.Sort(SortGenotypeByFitness);
        }

        public int SortGenotypeByFitness(Genotype a, Genotype b)
        {
            if (a.adjustedFitness > b.adjustedFitness)
            {
                return -1;
            }
            else if (a.adjustedFitness == b.adjustedFitness)
            {
                return 0;
            }

            return 1;
        }

        public void CullToPortion(float portion)
        {
            if (members.Count <= 1)
            {
                return;
            }

            int remaining = (int)Math.Ceiling(members.Count * portion);
            members.RemoveRange(remaining, members.Count - remaining);
        }

        public void CullToOne()
        {
            if (members.Count <= 1)
            {
                return;
            }

            members.RemoveRange(1, members.Count - 1);
        }

        public void CalculateAdjustedFitnessSum()
        {
            float sum = 0.0f;
            int membersCount = members.Count;

            for (int i = 0; i < membersCount; i++)
            {
                sum += members[i].adjustedFitness;
            }

            fitnessSum = sum;
        }

        // Serialize: "topFitness,staleness&MEMBER1nMEMBER2nMEMBER3..." with
        // no trailing 'n' after the last member. The caller (Population.Save)
        // inserts '&' separators between species.
        public void WriteTo(StringBuilder sb)
        {
            sb.Append(topFitness);
            sb.Append(SerialDelim.COMMA);
            sb.Append(staleness);
            sb.Append(SerialDelim.SPECIES);

            int membersCount = members.Count;
            for (int j = 0; j < membersCount; j++)
            {
                members[j].WriteTo(sb);
                if (j != membersCount - 1)
                {
                    sb.Append(SerialDelim.MEMBER);
                }
            }
        }
    }

    public class Population
    {
        public static Population instance = null;

        public int GENERATION = 0;

        public int POPULATION_SIZE = 256;
        public int INPUTS = 126;
        public int OUTPUTS = 7;
        public int MAX_STALENESS = 15;

        public float PORTION = 0.2f;

        public List<Species> species;
        public List<Genotype> genetics;
        public List<Phenotype> population;

        public static void Initialise()
        {
            if (instance == null)
            {
                instance = new Population();
            }
        }

        public Population()
        {
            species = new List<Species>();
            genetics = new List<Genotype>();
            population = new List<Phenotype>();
        }

        public void GenerateBasePopulation(int size, int inputs, int outputs)
        {
            POPULATION_SIZE = size;
            INPUTS = inputs;
            OUTPUTS = outputs;

            for (int i = 0; i < POPULATION_SIZE; i++)
            {
                Genotype genotype = NetworkFactory.instance.CreateBaseGenotype(inputs, outputs);
                genetics.Add(genotype);

                AddToSpecies(genotype);
            }

            NetworkFactory.instance.RegisterBaseMarkings(inputs, outputs);

            for (int i = 0; i < POPULATION_SIZE; i++)
            {
                Mutation.instance.MutateAll(genetics[i]);
            }

            InscribePopulation();
        }

        public void NewGeneration()
        {
            CalculateAdjustedFitness();

            for (int i = 0; i < species.Count; i++)
            {
                species[i].SortMembers();
                species[i].CullToPortion(PORTION);

                if (species[i].members.Count <= 1)
                {
                    species.RemoveAt(i);
                    i--;
                }
            }

            UpdateStaleness();

            float fitnessSum = 0.0f;

            for (int i = 0; i < species.Count; i++)
            {
                species[i].CalculateAdjustedFitnessSum();
                fitnessSum += species[i].fitnessSum;
            }

            List<Genotype> children = new List<Genotype>();

            for (int i = 0; i < species.Count; i++)
            {
                int build = (int)(POPULATION_SIZE * (species[i].fitnessSum / fitnessSum)) - 1;

                for (int j = 0; j < build; j++)
                {
                    Genotype child = species[i].Breed();
                    children.Add(child);
                }

            }

            while (POPULATION_SIZE > species.Count + children.Count)
            {
                Genotype child = species[RNG.instance.gen.Next(0,species.Count)].Breed();
                children.Add(child);
            }

            for (int i = 0; i < species.Count; i++)
            {
                species[i].CullToOne();
            }

            int childrenCount = children.Count;

            for (int i = 0; i < childrenCount; i++)
            {
                AddToSpecies(children[i]);
            }

            genetics.Clear();

            for (int i = 0; i < species.Count; i++)
            {
                for (int j = 0; j < species[i].members.Count; j++)
                {
                    genetics.Add(species[i].members[j]);
                }
            }

            InscribePopulation();

            GENERATION++;
        }

        public void CalculateAdjustedFitness()
        {
            int speciesCount = species.Count;

            for (int i = 0; i < speciesCount; i++)
            {
                int membersCount = species[i].members.Count;

                for (int j = 0; j < membersCount; j++)
                {
                    species[i].members[j].adjustedFitness = species[i].members[j].fitness / membersCount;
                }
            }
        }

        public void UpdateStaleness()
        {
            int speciesCount = species.Count;

            for (int i = 0; i < speciesCount; i++)
            {
                if (speciesCount == 1)
                {
                    return;
                }

                float top = species[i].members[0].fitness;

                if (species[i].topFitness < top)
                {
                    species[i].topFitness = top;
                    species[i].staleness = 0;
                }
                else
                {
                    species[i].staleness++;
                }

                if (species[i].staleness >= MAX_STALENESS)
                {
                    species.RemoveAt(i);
                    i--;
                    speciesCount--;
                }
            }
        }

        public void InscribePopulation()
        {
            population.Clear();

            for (int i = 0; i < POPULATION_SIZE; i++)
            {
                genetics[i].fitness = 0.0f;
                genetics[i].adjustedFitness = 0.0f;

                Phenotype physical = new Phenotype();
                physical.InscribeGenotype(genetics[i]);
                physical.ProcessGraph();

                population.Add(physical);
            }
        }

        public void AddToSpecies(Genotype genotype)
        {
            if (species.Count == 0)
            {
                Species new_species = new Species();
                new_species.members.Add(genotype);

                species.Add(new_species);
            }
            else
            {
                int speciesCount = species.Count;

                bool found = false;

                for (int i = 0; i < speciesCount; i++)
                {
                    float distance = Crossover.instance.SpeciationDistance(species[i].members[0], genotype);

                    if (distance < Crossover.instance.DISTANCE)
                    {
                        species[i].members.Add(genotype);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Species new_species = new Species();
                    new_species.members.Add(genotype);

                    species.Add(new_species);
                }
            }
        }
        public int SortGenotypeByFitness(Genotype a, Genotype b)
        {
            if (a.fitness > b.fitness)
            {
                return -1;
            }
            else if (a.fitness == b.fitness)
            {
                return 0;
            }

            return 1;
        }

        // Save the full population state — generation counter, champion score
        // (held by the caller's Tournament), historical innovation markings,
        // and every species + member — to a single delimited text file.
        //
        // The on-disk format is preserved byte-identical to what the original
        // Program.SaveState wrote, so the included monopoly_population *.txt
        // files load unchanged.
        public void Save(string path, float championScore)
        {
            Console.WriteLine("SAVING POPULATION");

            StringBuilder sb = new StringBuilder();

            sb.Append(GENERATION);
            sb.Append(SerialDelim.MAIN);
            sb.Append(championScore);
            sb.Append(SerialDelim.MAIN);

            int markings = 0;
            int historicalCount = Mutation.instance.historical.Count;
            for (int i = 0; i < historicalCount; i++)
            {
                Mutation.instance.historical[i].WriteTo(sb);
                if (i != historicalCount - 1)
                {
                    sb.Append(SerialDelim.COMMA);
                }
                markings++;
            }

            sb.Append(SerialDelim.MAIN);

            int speciesCount = species.Count;
            int geneCount = 0;
            for (int i = 0; i < speciesCount; i++)
            {
                species[i].WriteTo(sb);
                // Per-member progress logging preserved from the original
                // SaveState — handy when saving a large population.
                for (int j = 0; j < species[i].members.Count; j++)
                {
                    geneCount++;
                    Console.WriteLine(geneCount + "/" + genetics.Count);
                }
                if (i != speciesCount - 1)
                {
                    sb.Append(SerialDelim.SPECIES);
                }
            }

            sb.Append(SerialDelim.MAIN);

            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.Write(sb.ToString());
            }

            Console.WriteLine(markings + " MARKINGS");
        }

        // Load the population state written by Save. Returns the champion
        // score via out parameter (since it lives on Tournament, not here);
        // populates GENERATION, Mutation.instance.historical, this.species,
        // this.genetics, and rebuilds this.population via InscribePopulation.
        public void Load(string path, out float championScore)
        {
            string load;
            using (StreamReader sr = new StreamReader(path))
            {
                load = sr.ReadToEnd();
            }

            string[] parts = load.Split(SerialDelim.MAIN);

            int gen = int.Parse(parts[0]);
            float score = float.Parse(parts[1]);

            GENERATION = gen;
            championScore = score;

            string markingString = parts[2];
            string[] markingParts = markingString.Split(SerialDelim.COMMA);
            for (int i = 0; i < markingParts.GetLength(0); i += 3)
            {
                Marking m = new Marking();
                m.order = int.Parse(markingParts[i]);
                m.source = int.Parse(markingParts[i + 1]);
                m.destination = int.Parse(markingParts[i + 2]);
                Mutation.instance.historical.Add(m);
            }

            string networkString = parts[3];
            string[] speciesParts = networkString.Split(SerialDelim.SPECIES);

            for (int x = 0; x < speciesParts.GetLength(0); x += 2)
            {
                string[] firstParts = speciesParts[x].Split(SerialDelim.COMMA);

                Species s = new Species();
                s.topFitness = float.Parse(firstParts[0]);
                s.staleness = int.Parse(firstParts[1]);
                species.Add(s);

                string[] networkParts = speciesParts[x + 1].Split(SerialDelim.MEMBER);
                for (int i = 0; i < networkParts.GetLength(0); i++)
                {
                    Genotype genotype = Genotype.Parse(networkParts[i]);
                    s.members.Add(genotype);
                    genetics.Add(genotype);
                }
            }

            InscribePopulation();
        }
    }
}
