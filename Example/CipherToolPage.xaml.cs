using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FitnessCalculator;
using SubstitutionCipher;

namespace Example
{
    public partial class CipherToolPage : Page
    {
        private static readonly HashSet<char> _englishAlphabet = new HashSet<char>()
            {
                'A',    'B',    'C',    'D',
                'E',    'F',    'G',    'H',
                'I',    'J',    'K',    'L',
                'M',    'N',    'O',    'P',
                'Q',    'R',    'S',    'T',
                'U',    'V',    'W',    'X',
                'Y',    'Z'
            };
        private static readonly SemaphoreSlim _jobSemaphore = new SemaphoreSlim(1, 1);
        private static SubstitutionCipherMachine _cipherMachine;
        private static TextFitnessCalculator _textFitnessCalculator;

        public CipherToolPage()
        {
            InitializeComponent();

            _cipherMachine = new SubstitutionCipherMachine(_englishAlphabet);

            QuadgramDataset dataset = new QuadgramDataset(_englishAlphabet);

            Utils.ParseAndFill(dataset, "quadgrams_eng.txt");

            _textFitnessCalculator = new TextFitnessCalculator(dataset);

            Random random = new Random();

            TextBoxKey.Text = new string(TextBoxKey.Text.OrderBy(x => random.Next()).ToArray());
        }

        private async void EncodeButton_Click(object sender, RoutedEventArgs e)
        {
            bool entered = false;

            try
            {
                entered = await _jobSemaphore.WaitAsync(TimeSpan.Zero);

                if (!entered)
                {
                    return;
                }

                if (TryGetKey(out var key))
                {
                    ClearOutput();

                    var text = GetInputText();

                    string result = await Task.Run(() => _cipherMachine.EncodeWithIgnoring(text, key));

                    WriteResult(result);
                }
            }
            finally
            {
                if (entered)
                {
                    _jobSemaphore.Release();
                }
            }
        }
        private async void DecodeButton_Click(object sender, RoutedEventArgs e)
        {
            bool entered = false;

            try
            {
                entered = await _jobSemaphore.WaitAsync(TimeSpan.Zero);

                if (!entered)
                {
                    return;
                }

                if (TryGetKey(out var key))
                {
                    ClearOutput();

                    var text = GetInputText();

                    string result = await Task.Run(() => _cipherMachine.DecodeWithIgnoring(text, key));

                    WriteResult(result);
                }
            }
            finally
            {
                if (entered)
                {
                    _jobSemaphore.Release();
                }
            }
        }
        private async void BreakerNavButton_Click(object sender, RoutedEventArgs e)
        {
            bool entered = false;

            try
            {
                entered = await _jobSemaphore.WaitAsync(TimeSpan.Zero);

                if (!entered)
                {
                    return;
                }

                if (TryGetBreakerParams(out int populationSize, out int maxGenerationsCount, out double mutationChance, out int maxGenesToMutate, out double thresholdFitness))
                {
                    ClearOutput();

                    var text = GetInputText();

                    var breaker = new SubstitutionCipherBreaker(_englishAlphabet, _textFitnessCalculator, text);

                    string result = await Task.Run(() => breaker.Break(populationSize, maxGenerationsCount, mutationChance, maxGenesToMutate, thresholdFitness, Console.Out));

                    WriteResult(result);
                }
            }
            finally
            {
                if (entered)
                {
                    _jobSemaphore.Release();
                }
            }
        }

        private void WriteResult(string text)
        {
            TextBoxOutput.Text = text;
        }
        private void ClearOutput()
        {
            TextBoxOutput.Clear();
        }
        private string GetInputText()
        {
            return TextBoxInput.Text;
        }
        private void DragTextButton_Click(object sender, RoutedEventArgs e)
        {
            TextBoxInput.Text = TextBoxOutput.Text;
            ClearOutput();
        }

        private bool TryGetKey(out string key)
        {
            key = default;

            var keyInput = new string(TextBoxKey.Text.Distinct().Select(x => char.ToUpper(x)).ToArray());

            if (keyInput.Length != _englishAlphabet.Count)
            {
                MessageBox.Show("Error: Key must be a permutation of the English alphabet");
                return false;
            }

            foreach (var symb in keyInput)
            {
                if (!_englishAlphabet.Contains(symb))
                {
                    MessageBox.Show("Error: Key must be a permutation of the English alphabet");
                    return false;
                }
            }

            key = keyInput;

            return true;
        }
        private bool TryGetBreakerParams(out int populationSize, out int generationsNumber, out double mutationChance, out int maxGenesToMutate, out double thresholdFitness)
        {
            populationSize = default;
            generationsNumber = default;
            mutationChance = default;
            maxGenesToMutate = default;
            thresholdFitness = default;

            try
            {
                populationSize = int.Parse(TextBox_PS.Text);
                generationsNumber = int.Parse(TextBox_GN.Text);
                mutationChance = double.Parse(TextBox_MC.Text);
                maxGenesToMutate = int.Parse(TextBox_MG.Text);
                thresholdFitness = double.Parse(TextBox_TF.Text);

                if (populationSize < 0) throw new Exception("Population size must be possitive");
                else if (generationsNumber < 0) throw new Exception("Generations number must be possitive or null");
                else if (mutationChance <= 0 || mutationChance > 1) throw new Exception("Mutation chance is outside of (0, 1]");
                else if (maxGenesToMutate <= 0 || maxGenesToMutate > _englishAlphabet.Count) throw new Exception("Max genes to mutate must be positive and be less or equal than the alphabet length");
                else if (thresholdFitness < 0) throw new Exception("Threshold fitness mustn't be negative");

                return true;
            }
            catch (FormatException)
            {
                MessageBox.Show("Error: Wrong breaker params format");
                return false;
            }
            catch (OverflowException)
            {
                MessageBox.Show("Error: Wrong breaker params format");
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                return false;
            }
        }
    }
}
