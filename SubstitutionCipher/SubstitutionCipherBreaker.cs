using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using FitnessCalculator;

namespace SubstitutionCipher
{
    public class SubstitutionCipherBreaker
    {
        private readonly ArrayPool<byte> _textArrayPool;
        private readonly ArrayPool<byte> _keyArrayPool;
        private readonly ArrayPool<bool> _marksArrayPool;

        private readonly SubstitutionCipherMachine _cipherMachine;
        private readonly TextFitnessCalculator _fitnessCalculator;

        private readonly int _textLength;
        private readonly int _alphabetLength;
        private readonly byte[] _text;
        private readonly Dictionary<int, char> _symbols;

        private readonly HashSet<byte> _byteAlphabet;
        private HashSet<byte> CloneByteAlphabet => new HashSet<byte>(_byteAlphabet);

        public SubstitutionCipherBreaker(HashSet<char> alphabet, TextFitnessCalculator fitnessCalculator, string encodedText)
        {
            _cipherMachine = new SubstitutionCipherMachine(alphabet);
            _fitnessCalculator = fitnessCalculator;

            _text = _cipherMachine.ClearTextAndGetExternalSymbols(encodedText, out _symbols);

            _textLength = _text.Length;
            _alphabetLength = alphabet.Count;

            _textArrayPool = ArrayPool<byte>.Create(_textLength, 10);
            _keyArrayPool = ArrayPool<byte>.Create(_alphabetLength, 1);
            _marksArrayPool = ArrayPool<bool>.Create(_alphabetLength, 1);

            _byteAlphabet = new HashSet<byte>(Enumerable.Range(0, _alphabetLength).Select(x => (byte)x).ToArray());
        }

        public string Break(int populationSize, int generationsNumber, double mutationChance, int maxGenesToMutate, double thresholdFitness, TextWriter progressWriter = null)
        {
            Guard.Argument(populationSize, nameof(populationSize)).Positive();
            Guard.Argument(generationsNumber, nameof(generationsNumber)).Positive();
            Guard.Argument(mutationChance, nameof(maxGenesToMutate)).NotNegative().LessThan(1.0 + double.Epsilon);
            Guard.Argument(maxGenesToMutate, nameof(maxGenesToMutate)).NotNegative().LessThan(_alphabetLength + 1);
            Guard.Argument(thresholdFitness, nameof(thresholdFitness)).NotNegative();

            if (_fitnessCalculator.GetFitness(_text) < thresholdFitness)
            {
                return _cipherMachine.RepairTextWithExternalSymbols(_text, _symbols);
            }

            var list = new List<int>();
            for (int i = 0; i < populationSize; i++)
            {
                for (int j = i; j < populationSize; j++)
                {
                    list.Add(i);
                }
            }
            int[] probabilities = list.ToArray();

            Individual[] generation = GetFirstGeneration(populationSize);
            Individual[] extendedGeneration = new Individual[2 * populationSize];
            for (int i = 0; i < extendedGeneration.Length; i++)
            {
                extendedGeneration[i] = CreateNew();
            }

            Stopwatch sw = Stopwatch.StartNew();
            int gen = 1;

            for (; thresholdFitness < generation[0].Fitness && generationsNumber > gen; gen++)
            {
                progressWriter.WriteLine($"[Gen {gen,4}] {generation[0].Fitness}");

                Crossover(generation, extendedGeneration, probabilities);

                Mutation(extendedGeneration, mutationChance, maxGenesToMutate);

                Parallel.ForEach(extendedGeneration, CalculateFitness);
                Array.Sort(extendedGeneration);

                for (int j = 0; j < populationSize; j++)
                {
                    Array.Copy(extendedGeneration[j].Key, generation[j].Key, _alphabetLength);
                    generation[j].Fitness = extendedGeneration[j].Fitness;
                }
            }

            Array.Sort(generation);

            progressWriter.WriteLine($"\n[Total generations]   {gen}");
            progressWriter.WriteLine($"[Total time(sec)]     {sw.Elapsed.TotalSeconds}");
            progressWriter.WriteLine($"[Average ms/gen]      {(double)sw.ElapsedMilliseconds / gen}");
            progressWriter.WriteLine($"[Best fitness]        {generation[0].Fitness}");
            progressWriter.WriteLine($"[Best key]            {_cipherMachine.BytesToText(generation[0].Key, _alphabetLength)}\n");

            string result = _cipherMachine.RepairTextWithExternalSymbols(_cipherMachine.Decode(_text, generation[0].Key), _symbols);

            foreach (var individual in generation)
            {
                Free(individual);
            }
            foreach (var individual in extendedGeneration)
            {
                Free(individual);
            }

            return result;

            #region Helpers

            Individual[] GetFirstGeneration(int populationSize)
            {
                Random random = new Random(Guid.NewGuid().GetHashCode());
                Individual[] firstGeneration = new Individual[populationSize];

                for (int i = 0; i < populationSize; i++)
                {
                    firstGeneration[i] = CreateAndFill(CloneByteAlphabet.OrderBy(x => random.Next()).ToArray());
                }

                Parallel.ForEach(firstGeneration, CalculateFitness);
                Array.Sort(firstGeneration);

                return firstGeneration;
            }
            void CalculateFitness(Individual individual)
            {
                var textBuffer = _textArrayPool.Rent(_textLength);
                var subKeyBuffer = _keyArrayPool.Rent(_alphabetLength);

                _cipherMachine.DecodeWithoutAllocation(textBuffer, subKeyBuffer, _text, individual.Key);
                individual.Fitness = _fitnessCalculator.GetFitness(textBuffer, _textLength);

                _textArrayPool.Return(textBuffer);
                _keyArrayPool.Return(subKeyBuffer);
            }

            Individual CreateNew()
            {
                return new Individual(_keyArrayPool.Rent(_alphabetLength));
            }
            Individual CreateAndFill(byte[] key)
            {
                var individual = CreateNew();

                Array.Copy(key, individual.Key, _alphabetLength);

                return individual;
            }
            void Free(Individual individual)
            {
                _keyArrayPool.Return(individual.Key);

                individual.Key = null;
                individual.Fitness = double.NaN;
            }
            #endregion
        }
        private void Crossover(Individual[] generation, Individual[] extendedGeneration, int[] probabilities)
        {
            Random random = new Random(Guid.NewGuid().GetHashCode());

            for (int i = 0; i < extendedGeneration.Length; i += 2)
            {
                CrossoverOperator(SelectParents(), (extendedGeneration[i], extendedGeneration[i + 1]));
            }

            (Individual FirstParent, Individual SecondParent) SelectParents()
            {
                Individual first = generation[probabilities[random.Next(0, probabilities.Length)]];
                Individual second = first;

                while (first == second)
                {
                    second = generation[probabilities[random.Next(0, probabilities.Length)]];
                }

                return (first, second);
            }

            void CrossoverOperator((Individual FirstParent, Individual SecondParent) parents, (Individual FirstChild, Individual SecondChild) childs)
            {
                Individual firstParent = parents.FirstParent;
                Individual secondParent = parents.SecondParent;

                double firstParentFitness = firstParent.Fitness;
                double secondParentFitness = secondParent.Fitness;

                Individual better;
                Individual worse;

                if (firstParentFitness < secondParentFitness)
                {
                    better = firstParent;
                    worse = secondParent;
                }
                else
                {
                    better = secondParent;
                    worse = firstParent;
                }

                double chanceToSelectBetter = worse.Fitness / (firstParentFitness + secondParentFitness);

                byte[] firstKey = childs.FirstChild.Key;
                byte[] secondKey = childs.SecondChild.Key;

                HashSet<byte> left = CloneByteAlphabet;
                var used = _marksArrayPool.Rent(_alphabetLength);

                FillKey(firstKey);

                left = CloneByteAlphabet;
                for (int i = 0; i < used.Length; i++)
                {
                    used[i] = false;
                }

                FillKey(secondKey);

                _marksArrayPool.Return(used, true);

                void FillKey(byte[] key)
                {
                    for (int i = 0; i < _alphabetLength; i++)
                    {
                        bool u_1 = used[better.Key[i]];
                        bool u_2 = used[worse.Key[i]];
                        byte value;

                        if ((u_1 && !u_2) || (u_2 && !u_1))
                        {
                            value = u_2 ? better.Key[i] : worse.Key[i];
                        }
                        else if (!u_1 && !u_2)
                        {
                            value = random.NextDouble() < chanceToSelectBetter ? better.Key[i] : worse.Key[i];
                        }
                        else
                        {
                            value = left.ElementAt(random.Next(0, left.Count));
                        }

                        key[i] = value;
                        used[value] = true;
                        left.Remove(value);
                    }
                }
            }
        }
        private void Mutation(Individual[] extendedGeneration, double mutationChance, int maxGenesToMutate)
        {
            Random random = new Random(Guid.NewGuid().GetHashCode());

            for (int i = 0; i < extendedGeneration.Length; i++)
            {
                if (random.NextDouble() <= mutationChance)
                {
                    MutationOperator(extendedGeneration[i], random.Next(1, maxGenesToMutate));
                }
            }

            void MutationOperator(Individual individual, int genesToMutateCount)
            {
                for (int i = 0; i < genesToMutateCount; i++)
                {
                    int firstGenIndex = random.Next(0, _alphabetLength);
                    int secondGenIndex = firstGenIndex;

                    while (firstGenIndex == secondGenIndex)
                    {
                        secondGenIndex = random.Next(0, _alphabetLength);
                    }

                    byte buffer = individual.Key[firstGenIndex];
                    individual.Key[firstGenIndex] = individual.Key[secondGenIndex];
                    individual.Key[secondGenIndex] = buffer;
                }
            }
        }

        private class Individual : IComparable<Individual>
        {
            public byte[] Key;
            public double Fitness = double.PositiveInfinity;

            public Individual(byte[] gen)
            {
                Key = gen;
            }

            public int CompareTo([AllowNull] Individual other)
            {
                return Fitness.CompareTo(other.Fitness);
            }
        }
    }
}