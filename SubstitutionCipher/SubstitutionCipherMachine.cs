using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Dawn;

namespace SubstitutionCipher
{
    public class SubstitutionCipherMachine
    {
        private readonly Dictionary<char, byte> _posByChar = new Dictionary<char, byte>();
        private readonly Dictionary<byte, char> _charByPos = new Dictionary<byte, char>();
        private readonly int _alphabetLength;

        public SubstitutionCipherMachine(HashSet<char> alphabet)
        {
            Guard.Argument(alphabet, nameof(alphabet)).NotNull();
            Guard.Argument(alphabet, nameof(alphabet)).Require(alphabet.Count <= byte.MaxValue, x => "Alphabet must be no longer than 256 characters");

            var alph_arr = alphabet.Select(x => char.ToUpper(x)).ToArray();
            _alphabetLength = alph_arr.Length;

            for (byte i = 0; i < _alphabetLength; i++)
            {
                _posByChar[_charByPos[i] = alph_arr[i]] = i;
            }
        }

        public string EncodeWithIgnoring(string sourceText, string key)
        {
            byte[] text = ClearTextAndGetExternalSymbols(sourceText, out var symbols);

            return RepairTextWithExternalSymbols(Encode(text, TextToBytes(key)), symbols);
        }
        public string DecodeWithIgnoring(string encodedText, string key)
        {
            byte[] text = ClearTextAndGetExternalSymbols(encodedText, out var symbols);

            return RepairTextWithExternalSymbols(Decode(text, TextToBytes(key)), symbols);
        }
        public string EncodeWithClearing(string sourceText, string key)
        {
            return BytesToText(Encode(ClearText(sourceText), TextToBytes(key)));
        }
        public string DecodeWithClearing(string encodedText, string key)
        {
            return BytesToText(Decode(ClearText(encodedText), TextToBytes(key)));
        }

        public byte[] Encode(byte[] sourceText, byte[] key)
        {
            byte[] encodedText = new byte[sourceText.Length];

            EncodeWithoutAllocation(encodedText, encodedText.Length, sourceText, key);

            return encodedText;
        }
        public byte[] Decode(byte[] encodedText, byte[] key)
        {
            byte[] decodedText = new byte[encodedText.Length];
            byte[] subkey = new byte[_alphabetLength];

            DecodeWithoutAllocation(decodedText, subkey, encodedText, key);

            return decodedText;
        }
        public void EncodeWithoutAllocation(byte[] bufferForEncodedText, int textLength, byte[] sourceText, byte[] key)
        {
            for (int i = 0; i < textLength; i++)
            {
                bufferForEncodedText[i] = key[sourceText[i]];
            }
        }
        public void DecodeWithoutAllocation(byte[] bufferForDecodedText, byte[] bufferForSubkey, byte[] encodedText, byte[] key)
        {
            for (int i = 0; i < _alphabetLength; i++)
            {
                bufferForSubkey[i] = key.First(x => key[x] == i);
            }

            for (int i = 0; i < encodedText.Length; i++)
            {
                bufferForDecodedText[i] = bufferForSubkey[encodedText[i]];
            }
        }

        public string BytesToText(byte[] bytes, int textLength)
        {
            return new string(bytes.Take(textLength).Select(x => _charByPos[x]).ToArray());
        }
        public string BytesToText(byte[] bytes)
        {
            return BytesToText(bytes, bytes.Length);
        }
        public byte[] TextToBytes(string text)
        {
            return text.Select(x => _posByChar[x]).ToArray();
        }
        public byte[] ClearText(string text)
        {
            List<byte> bytes = new List<byte>(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                var c = char.ToUpper(text[i]);

                if (_posByChar.ContainsKey(c))
                {
                    bytes.Add(_posByChar[c]);
                }
            }

            return bytes.ToArray();
        }
        public byte[] ClearTextAndGetExternalSymbols(string text, out Dictionary<int, char> symbols)
        {
            symbols = new Dictionary<int, char>();

            List<byte> bytes = new List<byte>(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                var c = char.ToUpper(text[i]);

                if (_posByChar.ContainsKey(c))
                {
                    bytes.Add(_posByChar[c]);
                }
                else
                {
                    symbols[i] = c;
                }
            }

            return bytes.ToArray();
        }
        public string RepairTextWithExternalSymbols(byte[] bytes, Dictionary<int, char> symbols)
        {
            StringBuilder builder = new StringBuilder(bytes.Length + symbols.Count);

            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(_charByPos[bytes[i]]);
            }

            foreach (var pair in symbols)
            {
                builder.Insert(pair.Key, pair.Value);
            }

            return builder.ToString();
        }
    }
}