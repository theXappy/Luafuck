using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Luafuck
{
    public class ScriptObfuscator
    {
        internal class StringVariable
        {
            /// <summary>
            /// The string saved in the variable
            /// </summary>
            public string Content { get; private set; }
            /// <summary>
            /// An expression that can be used to access the variable
            /// </summary>
            public ExpressionSyntax Accessor { get; private set; }
            /// <summary>
            /// Length of the inner string
            /// </summary>
            public int Length => Content.Length;
            public StringVariable(string content, ExpressionSyntax accessor)
            {
                Content = content;
                Accessor = accessor;
            }
        }

        /// <summary>
        /// Variable name of the global table (Lua's default is _G)
        /// </summary>
        /// 
        string _globalTableVar;
        /// <summary>
        /// The variable name that holds the pointer to 'string.char'
        /// </summary>
        string _stringCharVar = null;

        public bool HasStringCharFunc => _stringCharVar != null;

        // This is part of the Lua language
        const string LUA_DEFAULT_GLOBAL_TABLE_VAR = "_G";


        /// <summary>
        /// Obfuscate with a really long single concatination chain of strings (each of 1 char) in a statment
        /// Stack is at risk because each concatination counts as a function call
        /// </summary>
        public SyntaxTree Obfuscate(SyntaxTree tree)
        {
            _globalTableVar = LUA_DEFAULT_GLOBAL_TABLE_VAR;

            // Getting any string -- the one we built above is good enough
            PrefixExpressionSyntax randomStringVariable = FuckedSyntaxFactory.DoubleBracketsString().Parenthesize();

            // ===
            // === Helper 1 -- Shorten string.char access
            // ===
            string varNameForStringChar = "_";
            PrefixExpressionSyntax varAccessorForLiteralChar = SyntaxFactory.IdentifierName(varNameForStringChar);
            var helper1StringExpr = "_G[" +
                    // Constructing "loadstring" string
                    "([[]]).char(#[[.]]..#_G..#[[........]]).." +
                    "([[]]).char(#[[.]]..#[[.]]..#[[.]]).." +
                    "[[a]].." +
                    "([[]]).char(#[[.]]..#_G..#_G).." +
                    "([[]]).char(#[[.]]..#[[.]]..#[[.....]]).." +
                    "([[]]).char(#[[.]]..#[[.]]..#[[......]]).." +
                    "[[r]].." +
                    "([[]]).char(#[[.]]..#_G..#[[.....]]).." +
                    "([[]]).char(#[[.]]..#[[.]]..#_G).." +
                    "([[]]).char(#[[.]]..#_G..#[[...]])" +
                "]" +
                    "(" +
                        // Constructing "_=([[char]])" string
                        "[[_]].." +
                        "([[]]).char(#[[......]]..#[[.]]).." +
                        "[[([[char]].." +
                        "([[]]).char(#[[.........]]..#[[...]]).." +
                        "[[])]]" +
                    ")()";


            var compileHelper1Expr = SyntaxFactory.ParseExpression(helper1StringExpr);

            // Combing both expressions above to "_[_]"
            PrefixExpressionSyntax funcAccessorForStringChar = SyntaxFactory.ElementAccessExpression(varAccessorForLiteralChar, varAccessorForLiteralChar);

            List<StringVariable> stringBuildingCheats = new()
            {
                new("char", varAccessorForLiteralChar)
            };


            // Creating a the string "loadstring" again, with less characters
            var loadstringShortcut = "_G[" +
                    // Constructing "loadstring" string
                    "_[_](#[[.]]..#_G..#[[........]]).." +
                    "_[_](#[[.]]..#[[.]]..#[[.]]).." +
                    "[[a]].." +
                    "_[_](#[[.]]..#_G..#_G).." +
                    "_[_](#[[.]]..#[[.]]..#[[.....]]).." +
                    "_[_](#[[.]]..#[[.]]..#[[......]]).." +
                    "[[r]].." +
                    "_[_](#[[.]]..#_G..#[[.....]]).." +
                    "_[_](#[[.]]..#[[.]]..#_G).." +
                    "_[_](#[[.]]..#_G..#[[...]])" +
                "]";

            // ===
            // === Helper 2 -- Shorten 'loadstring' string access
            // ===
            PrefixExpressionSyntax loadstringHandle3 = SyntaxFactory.ParseExpression(loadstringShortcut) as PrefixExpressionSyntax;

            // ===
            // === Helper 3 -- Create decoder function 'r'
            // ===

            var decoderFuncCode = $"function r(c)h=_.gsub return {varAccessorForLiteralChar}[{varAccessorForLiteralChar}]((h(h(h(h(h(h(h(h(h(h(c,'%(','0'),'%)','1'),'%.','2'),'_','3'),'#','4'),'c','5'),'h','6'),'a','7'),'r','8'),'G','9')))end";
            var decoderFuncStringExp = CreateString(funcAccessorForStringChar, decoderFuncCode, stringBuildingCheats);
            var compileHelper3Expr = SyntaxFactoryEx.FuncCall(SyntaxFactoryEx.FuncCall(loadstringHandle3, decoderFuncStringExp));

            PrefixExpressionSyntax decoderFuncHandle = SyntaxFactory.IdentifierName("r");


            // ===
            // === Create original code as a string
            // ===            
            var originalCode = tree.ToString();
            var encodedOriginalCode = CreateStringEncoded(funcAccessorForStringChar, decoderFuncHandle, originalCode);

            // ===
            // ===  Wrapping original code in a 'loadstring' call
            // ===
            var runOriginalCodeInLoadstring = SyntaxFactoryEx.FuncCall(SyntaxFactoryEx.FuncCall(loadstringHandle3, encodedOriginalCode));

            List<StatementSyntax> finalStatmentsList = new()
            {
                SyntaxFactory.ExpressionStatement(compileHelper1Expr),
                SyntaxFactory.ExpressionStatement(compileHelper3Expr),
                SyntaxFactory.ExpressionStatement(runOriginalCodeInLoadstring),
            };

            var obfusTree = SyntaxFactoryEx.SyntaxTree(finalStatmentsList);
            return obfusTree;
        }

        private VariableExpressionSyntax CreateLoadstringHandle(ExpressionSyntax loadstringString)
        {
            if (loadstringString is LiteralExpressionSyntax)
            {
                // Get the expression "("loadstring")"
                var parenthesizedLoadstring = SyntaxFactoryEx.Parenthesize(loadstringString);

                // Accesing _G with "loadstring"
                var G = SyntaxFactory.IdentifierName(_globalTableVar);
                var loadstringHandle = SyntaxFactory.ElementAccessExpression(G, parenthesizedLoadstring);

                return loadstringHandle;
            }
            else
            {
                // No paran required
                var G = SyntaxFactory.IdentifierName(_globalTableVar);
                var loadstringHandle = SyntaxFactory.ElementAccessExpression(G, loadstringString);

                return loadstringHandle;
            }
        }

        Dictionary<int, char> _digitsToEncodedChars = new()
        {
            { 0, '(' },
            { 1, ')' },
            { 2, '.' },
            { 3, '_' },
            { 4, '#' },
            { 5, 'c' },
            { 6, 'h' },
            { 7, 'a' },
            { 8, 'r' },
            { 9, 'G' }
        };

        private Dictionary<byte, ExpressionSyntax> _createStringEncodedCache = new();
        private ExpressionSyntax CreateStringEncoded(PrefixExpressionSyntax string_char_func, PrefixExpressionSyntax decoderFunc, string textContent)
        {
            List<ExpressionSyntax> expressions = new(textContent.Length);
            char[] knownChars = new char[] { 'a', 'c', 'h', 'r', ')', '(', '_', '.', '[', 'G', '#', };
            foreach (char c in textContent)
            {
                ExpressionSyntax charExpression = null;
                //
                // Handle characters within the charset (except ']')
                //
                if ((knownChars.Contains(c)))
                {
                    ExpressionSyntax lastExp = expressions.LastOrDefault();
                    if (lastExp != null && lastExp is LiteralExpressionSyntax litSyntax && litSyntax.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        // Last string was ALSO a string literal so let's just combine them :)
                        var prevLiteral = litSyntax.Token.Value as string;

                        // Remove old literal from list because we want to add a unified literal instead
                        expressions.Remove(lastExp);

                        // Add new literal
                        expressions.Add(FuckedSyntaxFactory.DoubleBracketsString(prevLiteral + c));

                    }
                    else
                    {
                        charExpression = FuckedSyntaxFactory.DoubleBracketsString(c.ToString());
                        expressions.Add(charExpression);
                    }
                }
                else
                {
                    // Char not in charset -- use decoder helper function.
                    // Encoding it's ASCII value in base 10 using 10 of the characters in the charset
                    byte asciiValue = (byte)c;
                    ExpressionSyntax currCharCreator;

                    // These expressions always end up the save for every ASCII value, so we cache them to save time.
                    // trying to get the cached value here or otherwise it's an unseen value so we do the hard work.
                    if (!_createStringEncodedCache.TryGetValue(asciiValue, out currCharCreator))
                    {
                        string encodedString = new string(asciiValue.ToString().Select(digitChar => _digitsToEncodedChars[digitChar - '0']).ToArray());

                        currCharCreator = SyntaxFactoryEx.FuncCall(decoderFunc, FuckedSyntaxFactory.DoubleBracketsString(encodedString));
                        _createStringEncodedCache[asciiValue] = currCharCreator;
                    }

                    expressions.Add(currCharCreator);
                }
            }
            ExpressionSyntax aggregator = null;
            aggregator = SyntaxFactoryEx.ConcatStrings(expressions);

            if (aggregator is BinaryExpressionSyntax binExp)
            {
                aggregator = ReorginizeConcatExpression(binExp);
            }

            return aggregator;

        }

        /// <param name="alreadyCompiledStrings">Used to 'cheat' if we already have some string we can use their lengths to shorten the process</param>
        private ExpressionSyntax CreateString(PrefixExpressionSyntax string_char_func, string textContent, List<StringVariable> alreadyCompiledStrings = null)
        {
            alreadyCompiledStrings ??= new();

            List<ExpressionSyntax> expressions = new();
            char[] knownChars = new char[] { 'a', 'c', 'h', 'r', ')', '(', '_', '.', '[', 'G', '#', };
            foreach (char c in textContent)
            {
                ExpressionSyntax charExpression = null;
                //
                // Handle characters within the charset (except ']')
                //
                if ((knownChars.Contains(c)))
                {
                    ExpressionSyntax lastExp = expressions.LastOrDefault();
                    if (lastExp != null && lastExp is LiteralExpressionSyntax litSyntax && litSyntax.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        // Last string was ALSO a string literal so let's just combine them :)
                        var prevLiteral = litSyntax.Token.Value as string;

                        // Remove old literal from list because we want to add a unified literal instead
                        expressions.Remove(lastExp);

                        // Add new literal
                        expressions.Add(FuckedSyntaxFactory.DoubleBracketsString(prevLiteral + c));

                    }
                    else
                    {
                        charExpression = FuckedSyntaxFactory.DoubleBracketsString(c.ToString());
                        expressions.Add(charExpression);
                    }
                }
                else
                {
                    // Getting ASCII value for char
                    byte asciiValue = (byte)c;

                    int[] digits = asciiValue.ToString().Select(asciiDigit => asciiDigit - '0').ToArray();

                    ExpressionSyntax[] digitsStringsExpressions = digits.Select(digit => CreateDigit(digit, alreadyCompiledStrings)).ToArray();
                    var digitsConcatination = SyntaxFactoryEx.ConcatStrings(digitsStringsExpressions);

                    // Calling 'string.char' on the string containing all digits (effecitvly the ascii value)
                    charExpression = SyntaxFactoryEx.FuncCall(string_char_func, digitsConcatination);

                    // Finally add this char expression to the list of all expressions
                    expressions.Add(charExpression);

                }
            }

            ExpressionSyntax aggregator = SyntaxFactoryEx.ConcatStrings(expressions);

            if (aggregator is BinaryExpressionSyntax binExp)
            {
                aggregator = ReorginizeConcatExpression(binExp);
            }

            return aggregator;
        }

        private int _craeteDigitCacheLastCheatsLength = 0;
        private Dictionary<int, ExpressionSyntax> _createDigitCache = new();
        private ExpressionSyntax CreateDigit(int digit, List<StringVariable> alreadyCompiledStrings = null)
        {
            if (_craeteDigitCacheLastCheatsLength != alreadyCompiledStrings.Count)
            {
                _createDigitCache.Clear();
                _craeteDigitCacheLastCheatsLength = alreadyCompiledStrings.Count;
            }

            if (_createDigitCache.TryGetValue(digit, out ExpressionSyntax cached))
            {
                // Cache hit!
                return cached;
            }

            ExpressionSyntax newExp = null;
            if (digit == 0)
            {
                newExp = SyntaxFactory.ParseExpression("#_G");
                _createDigitCache.Add(digit, newExp);
                return newExp;
            }

            // Check for EXACT cheat match
            var matchingCheat = alreadyCompiledStrings.FirstOrDefault(existingVariable => existingVariable.Length == digit);
            if (matchingCheat != null)
            {
                newExp = SyntaxFactoryEx.LengthExpression(matchingCheat.Accessor);
                _createDigitCache.Add(digit, newExp);
                return newExp;
            }
            // TODO: Also allow partial cheats (for example if 'a' holds a 8-character-long string and we need to make 9 we should do:
            // #(a..[[.]])
            // instead of 
            // #[[.........]]
            // )
            // Not quite sure what's the crateria to decide when it's worth it or not...
            string naiveExpressionString = $"#[[{new string('.', digit)}]]";
            newExp = SyntaxFactory.ParseExpression(naiveExpressionString);
            _createDigitCache.Add(digit, newExp);
            return newExp;
        }

        /// <summary>
        /// Adds parenthesis to binary expressiones to reduce function calls stack depth.
        /// e.g. "a .. b .. c .. d" will turn into "((a .. b) .. (c .. d))"
        /// </summary>
        /// <param name="binExp">Binary expression to reorginize</param>
        /// <returns>Reorginized expression</returns>
        public static ExpressionSyntax ReorginizeConcatExpression(BinaryExpressionSyntax binExp)
        {
            // Collect all expressions used within the binary expressions chain
            // e.g. "a .. b .. c .. d" the expressions "a","b","c","d" will be collected
            List<ExpressionSyntax> parts = new List<ExpressionSyntax>();
            BinaryExpressionSyntax curr = binExp;
            parts.Add(curr.Right);
            while (curr.Left is BinaryExpressionSyntax next)
            {
                curr = next;
                parts = parts.Prepend(curr.Right).ToList();
            }
            parts = parts.Prepend(curr.Left).ToList();


            // Now collect every 2 expressions from the collected expressions into a concat expression with parenthesis.
            // This is done in the inner for loop.
            //
            // The outter 'while' keeps doing this while the list have more then 1 item.
            // Which means it is done iterativly to "fold" all expressions into one big expression.
            //
            // Example:
            // Input a .. b .. c .. d .. e .. f .. g .. h <-- 8 parts
            // After while loop Iter 1: (a .. b) .. (c .. d) .. (e .. f) .. (g .. h)    <-- 4 parts
            // After while loop Iter 2: ((a .. b) .. (c .. d)) .. ((e .. f) .. (g .. h))  <-- 2 parts
            // After while loop Iter 3: (((a .. b) .. (c .. d)) .. ((e .. f) .. (g .. h))) <-- 1 part
            List<ExpressionSyntax> currentExpList = parts;
            List<ExpressionSyntax> newExpList = new();
            while (currentExpList.Count > 1)
            {
                for (int i = 0; i < currentExpList.Count; i += 2)
                {
                    if (i == currentExpList.Count - 1)
                    {
                        // Last item is alone
                        newExpList.Add(SyntaxFactoryEx.Parenthesize(currentExpList[i]));
                    }
                    else
                    {
                        ExpressionSyntax currLeft = currentExpList[i];
                        ExpressionSyntax currRight = currentExpList[i + 1];
                        newExpList.Add(SyntaxFactoryEx.Parenthesize(SyntaxFactoryEx.ConcatStrings(currLeft, currRight)));
                    }
                }
                currentExpList = newExpList;
                newExpList = new();
            }

            return currentExpList.Single();
        }
    }
}
