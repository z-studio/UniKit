/******************************************************************************
 * Spine Runtimes License Agreement
 * Last updated April 5, 2025. Replaces all prior versions.
 *
 * Copyright (c) 2013-2025, Esoteric Software LLC
 *
 * Integration of the Spine Runtimes into software or otherwise creating
 * derivative works of the Spine Runtimes is permitted under the terms and
 * conditions of Section 2 of the Spine Editor License Agreement:
 * http://esotericsoftware.com/spine-editor-license
 *
 * Otherwise, it is permitted to integrate the Spine Runtimes into software
 * or otherwise create derivative works of the Spine Runtimes (collectively,
 * "Products"), provided that each user of the Products must obtain their own
 * Spine Editor license and redistribution of the Products in any form must
 * include this license and copyright notice.
 *
 * THE SPINE RUNTIMES ARE PROVIDED BY ESOTERIC SOFTWARE LLC "AS IS" AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL ESOTERIC SOFTWARE LLC BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES,
 * BUSINESS INTERRUPTION, OR LOSS OF USE, DATA, OR PROFITS) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THE SPINE RUNTIMES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Spine {
    public static class Json {
        public static object Deserialize(TextReader text) {
            var parser = new SharpJson.JsonDecoder {
                parseNumbersAsFloat = true
            };

            return parser.Decode(text.ReadToEnd());
        }
    }
}

/**
 * Copyright (c) 2016 Adriano Tinoco d'Oliveira Rezende
 *
 * Based on the JSON parser by Patrick van Bergen
 * http://techblog.procurios.nl/k/news/view/14605/14863/how-do-i-write-my-own-parser-(for-json).html
 *
 * Changes made:
 *
 * - Optimized parser speed (deserialize roughly near 3x faster than original)
 * - Added support to handle lexer/parser error messages with line numbers
 * - Added more fine grained control over type conversions during the parsing
 * - Refactory API (Separate Lexer code from Parser code and the Encoder from Decoder)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without restriction,
 * including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all copies or substantial
 * portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
 * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
 * OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */
namespace SharpJson {
    internal class Lexer {
        public enum Token {
            None,
            Null,
            True,
            False,
            Colon,
            Comma,
            String,
            Number,
            CurlyOpen,
            CurlyClose,
            SquaredOpen,
            SquaredClose,
        };

        public bool hasError => !m_Success;

        public int lineNumber { get; private set; }

        public bool parseNumbersAsFloat { get; set; }

        private char[] m_Json;
        private int m_Index = 0;
        private bool m_Success = true;
        private char[] m_StringBuffer = new char[4096];

        public Lexer(string text) {
            Reset();

            m_Json = text.ToCharArray();
            parseNumbersAsFloat = false;
        }

        public void Reset() {
            m_Index = 0;
            lineNumber = 1;
            m_Success = true;
        }

        public string ParseString() {
            int idx = 0;
            StringBuilder builder = null;

            SkipWhiteSpaces();

            // "
            char c = m_Json[m_Index++];

            bool failed = false;
            bool complete = false;

            while (!complete && !failed) {
                if (m_Index == m_Json.Length) {
                    break;
                }

                c = m_Json[m_Index++];

                if (c == '"') {
                    complete = true;
                    break;
                } else if (c == '\\') {
                    if (m_Index == m_Json.Length) {
                        break;
                    }

                    c = m_Json[m_Index++];

                    switch (c) {
                        case '"':
                            m_StringBuffer[idx++] = '"';
                            break;
                        case '\\':
                            m_StringBuffer[idx++] = '\\';
                            break;
                        case '/':
                            m_StringBuffer[idx++] = '/';
                            break;
                        case 'b':
                            m_StringBuffer[idx++] = '\b';
                            break;
                        case 'f':
                            m_StringBuffer[idx++] = '\f';
                            break;
                        case 'n':
                            m_StringBuffer[idx++] = '\n';
                            break;
                        case 'r':
                            m_StringBuffer[idx++] = '\r';
                            break;
                        case 't':
                            m_StringBuffer[idx++] = '\t';
                            break;
                        case 'u':
                            int remainingLength = m_Json.Length - m_Index;

                            if (remainingLength >= 4) {
                                string hex = new string(m_Json, m_Index, 4);

                                // XXX: handle UTF
                                m_StringBuffer[idx++] = (char)Convert.ToInt32(hex, 16);

                                // skip 4 chars
                                m_Index += 4;
                            } else {
                                failed = true;
                            }

                            break;
                    }
                } else {
                    m_StringBuffer[idx++] = c;
                }

                if (idx >= m_StringBuffer.Length) {
                    builder ??= new StringBuilder();
                    builder.Append(m_StringBuffer, 0, idx);
                    idx = 0;
                }
            }

            if (!complete) {
                m_Success = false;
                return null;
            }

            return builder != null ? builder.ToString() : new string(m_StringBuffer, 0, idx);
        }

        private string GetNumberString() {
            SkipWhiteSpaces();

            int lastIndex = GetLastIndexOfNumber(m_Index);
            int charLength = (lastIndex - m_Index) + 1;

            string result = new string(m_Json, m_Index, charLength);

            m_Index = lastIndex + 1;

            return result;
        }

        public float ParseFloatNumber() {
            float number;
            string str = GetNumberString();

            if (!float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) {
                return 0;
            }

            return number;
        }

        public double ParseDoubleNumber() {
            double number;
            string str = GetNumberString();

            if (!double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out number)) {
                return 0;
            }

            return number;
        }

        private int GetLastIndexOfNumber(int index) {
            int lastIndex;

            for (lastIndex = index; lastIndex < m_Json.Length; lastIndex++) {
                char ch = m_Json[lastIndex];

                if ((ch < '0' || ch > '9')
                    && ch != '+'
                    && ch != '-'
                    && ch != '.'
                    && ch != 'e'
                    && ch != 'E')
                    break;
            }

            return lastIndex - 1;
        }

        private void SkipWhiteSpaces() {
            for (; m_Index < m_Json.Length; m_Index++) {
                char ch = m_Json[m_Index];

                if (ch == '\n') {
                    lineNumber++;
                }

                if (!char.IsWhiteSpace(m_Json[m_Index])) {
                    break;
                }
            }
        }

        public Token LookAhead() {
            SkipWhiteSpaces();

            int savedIndex = m_Index;
            return NextToken(m_Json, ref savedIndex);
        }

        public Token NextToken() {
            SkipWhiteSpaces();
            return NextToken(m_Json, ref m_Index);
        }

        private static Token NextToken(char[] json, ref int index) {
            if (index == json.Length) {
                return Token.None;
            }

            char c = json[index++];

            switch (c) {
                case '{':
                    return Token.CurlyOpen;
                case '}':
                    return Token.CurlyClose;
                case '[':
                    return Token.SquaredOpen;
                case ']':
                    return Token.SquaredClose;
                case ',':
                    return Token.Comma;
                case '"':
                    return Token.String;
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                case '-':
                    return Token.Number;
                case ':':
                    return Token.Colon;
            }

            index--;

            int remainingLength = json.Length - index;

            // false
            if (remainingLength >= 5) {
                if (json[index] == 'f'
                    && json[index + 1] == 'a'
                    && json[index + 2] == 'l'
                    && json[index + 3] == 's'
                    && json[index + 4] == 'e') {
                    index += 5;
                    return Token.False;
                }
            }

            // true
            if (remainingLength >= 4) {
                if (json[index] == 't' && json[index + 1] == 'r' && json[index + 2] == 'u' && json[index + 3] == 'e') {
                    index += 4;
                    return Token.True;
                }
            }

            // null
            if (remainingLength >= 4) {
                if (json[index] == 'n' && json[index + 1] == 'u' && json[index + 2] == 'l' && json[index + 3] == 'l') {
                    index += 4;
                    return Token.Null;
                }
            }

            return Token.None;
        }
    }

    public class JsonDecoder {
        public string errorMessage { get; private set; }

        public bool parseNumbersAsFloat { get; set; }

        private Lexer m_Lexer;

        public JsonDecoder() {
            errorMessage = null;
            parseNumbersAsFloat = false;
        }

        public object Decode(string text) {
            errorMessage = null;

            m_Lexer = new Lexer(text);
            m_Lexer.parseNumbersAsFloat = parseNumbersAsFloat;

            return ParseValue();
        }

        public static object DecodeText(string text) {
            var builder = new JsonDecoder();
            return builder.Decode(text);
        }

        private IDictionary<string, object> ParseObject() {
            var table = new Dictionary<string, object>();

            // {
            m_Lexer.NextToken();

            while (true) {
                Lexer.Token token = m_Lexer.LookAhead();

                switch (token) {
                    case Lexer.Token.None:
                        TriggerError("Invalid token");
                        return null;
                    case Lexer.Token.Comma:
                        m_Lexer.NextToken();
                        break;
                    case Lexer.Token.CurlyClose:
                        m_Lexer.NextToken();
                        return table;
                    default:
                        // name
                        string name = EvalLexer(m_Lexer.ParseString());

                        if (errorMessage != null) {
                            return null;
                        }

                        // :
                        token = m_Lexer.NextToken();

                        if (token != Lexer.Token.Colon) {
                            TriggerError("Invalid token; expected ':'");
                            return null;
                        }

                        // value
                        object value = ParseValue();

                        if (errorMessage != null) {
                            return null;
                        }

                        table[name] = value;
                        break;
                }
            }

            //return null; // Unreachable code
        }

        private IList<object> ParseArray() {
            var array = new List<object>();

            // [
            m_Lexer.NextToken();

            while (true) {
                Lexer.Token token = m_Lexer.LookAhead();

                switch (token) {
                    case Lexer.Token.None:
                        TriggerError("Invalid token");
                        return null;
                    case Lexer.Token.Comma:
                        m_Lexer.NextToken();
                        break;
                    case Lexer.Token.SquaredClose:
                        m_Lexer.NextToken();
                        return array;
                    default:
                        object value = ParseValue();

                        if (errorMessage != null) {
                            return null;
                        }

                        array.Add(value);
                        break;
                }
            }

            //return null; // Unreachable code
        }

        private object ParseValue() {
            switch (m_Lexer.LookAhead()) {
                case Lexer.Token.String:
                    return EvalLexer(m_Lexer.ParseString());
                case Lexer.Token.Number:
                    if (parseNumbersAsFloat) {
                        return EvalLexer(m_Lexer.ParseFloatNumber());
                    } else {
                        return EvalLexer(m_Lexer.ParseDoubleNumber());
                    }
                case Lexer.Token.CurlyOpen:
                    return ParseObject();
                case Lexer.Token.SquaredOpen:
                    return ParseArray();
                case Lexer.Token.True:
                    m_Lexer.NextToken();
                    return true;
                case Lexer.Token.False:
                    m_Lexer.NextToken();
                    return false;
                case Lexer.Token.Null:
                    m_Lexer.NextToken();
                    return null;
                case Lexer.Token.None:
                    break;
            }

            TriggerError("Unable to parse value");
            return null;
        }

        private void TriggerError(string message) {
            errorMessage = $"Error: '{message}' at line {m_Lexer.lineNumber}";
        }

        private T EvalLexer<T>(T value) {
            if (m_Lexer.hasError) {
                TriggerError("Lexical error ocurred");
            }

            return value;
        }
    }
}