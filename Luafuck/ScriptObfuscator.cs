using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Luafuck.Hash;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Luafuck
{
    public class ScriptObfuscator
    {
        internal record LuaFunctionName(string Name);

        internal class VariableNamesGenerator
        {
            Dictionary<string, string> varsMap = new();
            private IEnumerator<string> _mainGenerator;

            char[] availChars = new char[4] { '[', ']', '(', ')' };

            /// <summary>
            /// Endless generator function for string constructed of the characters '[]()' 
            /// </summary>
            /// <param name="startName">name to start prepending characters to</param>
            /// <returns>Enunmerable to produce the names</returns>
            private IEnumerable<string> generationFunc(string startName)
            {
                foreach (char c in availChars)
                {
                    yield return c + startName;
                }

                List<IEnumerator<string>> subGenerators = new();
                foreach (char c in availChars)
                {
                    string newString = c + startName;
                    IEnumerator<string> subGenerator = generationFunc(newString).GetEnumerator();
                    subGenerators.Add(subGenerator);
                }

                while (true)
                {
                    for (int i = 0; i < subGenerators.Count; i++)
                    {
                        subGenerators[i].MoveNext();
                        yield return subGenerators[i].Current;
                    }

                }
            }

            public VariableNamesGenerator()
            {
                _mainGenerator = generationFunc("[").GetEnumerator();
                _mainGenerator.MoveNext();
            }

            public string AllocVariable(string key)
            {
                if (varsMap.TryGetValue(key, out var name))
                {
                    return name;
                }

                string varName = _mainGenerator.Current;
                // "]]" is forbidden since it closes our double-brackets enclosed strings
                while (varName.Contains("]]") || varsMap.ContainsValue(varName))
                {
                    _mainGenerator.MoveNext();
                    varName = _mainGenerator.Current;
                }
                varsMap.Add(key, varName);
                return varName;
            }

            public string ResolveBack(string allocatedName) => varsMap.First(kvp => kvp.Value == allocatedName).Key;
        }

        /// <summary>
        /// Hold variable names for the "Building Blocks Strings" - string of sizes 1,2,4,8,16,32,64
        /// Index is the power of 2 e.g. [3] =>> var name for string of length (2^3)=8
        /// </summary>
        /// 
        string[] _buildingBlocksStringVars = new string[7];
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

        private VariableNamesGenerator _variableNamesGenerator;

        /// <summary>
        /// This dictionary is used for debug statments IF debugging is enabled
        /// </summary>
        private Dictionary<string, object> _debug_VarNameToExpectedValue = new();

        public ScriptObfuscator()
        {
            _variableNamesGenerator = new VariableNamesGenerator();
        }

        public SyntaxTree Obfuscate(SyntaxTree tree)
        {
            // Simplest obfuscation - Build primites and then compile code that uses them
            // to write the original Lua script in memory (in a variable) then call 'loadstring' on it.
            List<StatementSyntax> statements = new();
            // Shorten global table name: from '_G' to '_'
            // like that:
            // _ = _G["_G"]
            // I use the variable access expression so it ends with a bracket. This is crucial so that
            // I can start the next expression after it (if it ended with just `_G` the next expression characters would be mmisunderstood
            // as the rest of the identifier for example `_G_G[ ... ]`
            _globalTableVar = "_";
            statements.Add(SyntaxFactoryEx.AssignmentExpression(
                        SyntaxFactory.IdentifierName(_globalTableVar),
                        FuckedSyntaxFactory.FuckedVariableAccessExpression(LUA_DEFAULT_GLOBAL_TABLE_VAR, LUA_DEFAULT_GLOBAL_TABLE_VAR)
                        ));

            // Create primitives
            //      Strings in several lengths of powers of 2
            CreateBuildingBlocksStrings(statements);

            //      Function pointer for string.char
            var (stringCharStatements, stringCharVar) = CreateStringCharVariable();
            _stringCharVar = stringCharVar;
            statements.AddRange(stringCharStatements.Statements);

            // Construct original script's code as string in new code's memory (saved in a variable)
            string originalCode = tree.ToString();
            var (codeHolderCreationStatements, codeHolderVar) = GetStringLiteral(originalCode);
            statements.AddRange(codeHolderCreationStatements.Statements);

            // Get loadstring function
            var (loadstringNameCreationStatements, loadstringFuncHolderVar) = CreateLoadstringVariable();
            statements.AddRange(loadstringNameCreationStatements.Statements);

            // Invoke loadstring with original code's content to generate a FUNCTION
            var loadstringCompileCall = SyntaxFactoryEx.FuncCall(
                                        FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, loadstringFuncHolderVar),
                                        FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, codeHolderVar)
                                        );
            // Call returned FUNCTION to execute original code
            var loadstringExecuteCall = SyntaxFactoryEx.FuncCall(loadstringCompileCall, new ExpressionSyntax[0]);
            statements.Add(SyntaxFactory.ExpressionStatement(loadstringExecuteCall));


            // Add end of lines to all statments and possibly debug statements
            List<StatementSyntax> statementsWithEOLs = RefactorSyntaxStatements(statements);

            var obfusTree = SyntaxFactoryEx.SyntaxTree(statementsWithEOLs);
            return obfusTree;
        }

        /// <summary>
        /// Adds end of lines (\n) to all statments and, if debugging is enabled, adds debugging statements 
        /// (comment with original var names, print of values, print of types, assert of expected values for assignments)
        /// </summary>
        private List<StatementSyntax> RefactorSyntaxStatements(List<StatementSyntax> statements)
        {
            bool DEBUGGING = false;
            if(!DEBUGGING)
            {
                return statements;
            }

            List<StatementSyntax> statementsWithEOLs = new();

            // This dictionary will hold for every statement a list (possible of length 1) of statements derieved with it 
            // with injected trivia (new line characters) and possibly debugging statements
            Dictionary<StatementSyntax, List<StatementSyntax>> statementsToRefactoredStatments = new();
            foreach (StatementSyntax statement in statements)
            {
                var currList = new List<StatementSyntax>();
                statementsToRefactoredStatments[statement] = currList;

                var statementWithTrivia = statement.WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
                currList.Add(statementWithTrivia);

                if (!(statement is AssignmentStatementSyntax assgn))
                {
                    continue;
                }
                PrefixExpressionSyntax assgnVar = assgn.Variables.FirstOrDefault();
                if (!(assgnVar is ElementAccessExpressionSyntax elementAccess))
                {
                    continue;
                }
                // We found an element acces (i.e. AAA[BBB]), Assuming it's a _G[XXX] accessor
                ExpressionSyntax member = elementAccess.KeyExpression;
                while (member is ParenthesizedExpressionSyntax pes)
                {
                    // stripping parenthesis
                    member = pes.Expression;
                }

                if (!(member is LiteralExpressionSyntax les))
                {
                    continue;
                }
                // Finally! This is a statement where we can insert some debugging info for. So start by removing the naive assignment 
                // from the current node's "refactored statements list"
                // we'll re-add it with more info in a second
                currList.Clear();


                string obfsVarName = les.ExtractString();
                string resolvedOriginalVarName = this._variableNamesGenerator.ResolveBack(obfsVarName);

                statementWithTrivia = statement.WithTrailingTrivia(
                   SyntaxFactory.EndOfLine("\n"),
                   SyntaxFactory.Comment("-- " + resolvedOriginalVarName),
                   SyntaxFactory.EndOfLine("\n")
                       );
                currList.Add(statementWithTrivia);

                // Create assert expression to make sure the variable was assigned the right value
                string expToAssertString = elementAccess.ToString() + " == ";
                // Switching on the left-hand side of the assignment's type. For different types we write different assertions
                if (_debug_VarNameToExpectedValue.TryGetValue(obfsVarName, out object val))
                {
                    switch (val)
                    {
                        case char c:
                            // Lua chars are string of size 1
                            expToAssertString += $"[===[{c}]===]";
                            break;
                        case string s:
                            // Wrapping with the multi-line '[[' and ']]' strings
                            // the === are used so if '[[' and ']]' are already in the string it doesn't break the string prematurly
                            // using 3 equals allows up to 3 levels of inner strings in strings ([[,[=[,[==[)
                            // but I'm not expecting more than '[[',']] to be honest but this code breaks if there are too many
                            expToAssertString += $"[===[{s}]===]";
                            break;
                        case int num:
                            expToAssertString += num;
                            break;
                        case LuaFunctionName func:
                            expToAssertString += func.Name;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
                else
                {
                    expToAssertString = "true";
                }

                // Assert
                ExpressionSyntax expToAssert = SyntaxFactory.ParseExpression(expToAssertString);
                ExpressionStatementSyntax assertStatment = SyntaxFactory.ExpressionStatement(
                                    SyntaxFactoryEx.FuncCall(SyntaxFactory.IdentifierName("assert"), expToAssert));
                assertStatment = assertStatment.WithTrailingTrivia(
                    SyntaxFactory.EndOfLine("\n"));

                // print var value
                ExpressionStatementSyntax printStatement = SyntaxFactory.ExpressionStatement(
                                    SyntaxFactoryEx.FuncCall(SyntaxFactory.IdentifierName("print"), elementAccess));
                printStatement = printStatement.WithTrailingTrivia(
                    SyntaxFactory.EndOfLine("\n"));

                // print var type
                ExpressionStatementSyntax printTypeStatement = SyntaxFactory.ExpressionStatement(
                                    SyntaxFactoryEx.FuncCall(SyntaxFactory.IdentifierName("print"),
                                            SyntaxFactoryEx.FuncCall(SyntaxFactory.IdentifierName("type"), elementAccess)));
                printTypeStatement = printTypeStatement.WithTrailingTrivia(
                    SyntaxFactory.EndOfLine("\n"));


                // Add all statments to the list
                currList.Add(statementWithTrivia);
                currList.Add(printStatement);
                currList.Add(printTypeStatement);
                currList.Add(assertStatment);
                continue;

            }

            // Combine all refactored lists to a single list
            foreach (StatementSyntax statement in statements)
            {
                var refactoredList = statementsToRefactoredStatments[statement];
                statementsWithEOLs.AddRange(refactoredList);
            }

            return statementsWithEOLs;
        }

        private (StatementListSyntax creationStatements, string varName) CreateStringCharVariable()
        {
            // Right side of assignment: we want to call get the 'char' function of the empty string ('[[]]')
            // This is done with an indexed accessor: ([[]])['char']
            // But we dont user "'char'" since we are avoiding single qoutes
            // So we assign the string "char" to another variable and then using it as indexer
            var constStringCharLiteral = FuckedSyntaxFactory.DoubleBracketsString("char"); // [[char]]
            string constStringCharVarName = _variableNamesGenerator.AllocVariable("__CONSTSTRINGCHAR__");

            // This gives us _G[*var name*]
            // We will use it 2 times: 
            // 1. In the assignment of the literal "char" to it
            // 2. as an indexer to retrieve the "string.char" function 
            ElementAccessExpressionSyntax constStringChar = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, constStringCharVarName);
            var constStringCharAssn = SyntaxFactoryEx.AssignmentExpression(constStringChar, constStringCharLiteral);


            ExpressionSyntax emptyString = FuckedSyntaxFactory.FuckedEmptyString(); // [[]]
            ParenthesizedExpressionSyntax parenthesizedEmptyString = emptyString.Parenthesize(); // ([[]])
            var tableAccessToGetCharFunc = SyntaxFactory.ElementAccessExpression(parenthesizedEmptyString, constStringChar); // ([[]])[CHAR_VARIABLE]

            // Assigning variable for the function pointer
            string stringCharFuncVarName = _variableNamesGenerator.AllocVariable("__STRINGCHARFUNC__"); // TODO: make this const?
            var LeftSide = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, stringCharFuncVarName);

            var stringCharFuncAssn = SyntaxFactoryEx.AssignmentExpression(LeftSide, tableAccessToGetCharFunc);

            // Final statements list:
            // 1. Assignment of the string variable to hold "char" 
            // 2. Assignment of the func ptr variable to hold "string.char"
            var statements = SyntaxFactory.StatementList(constStringCharAssn, stringCharFuncAssn);

            return (statements, stringCharFuncVarName);
        }
        private (StatementListSyntax creationStatements, string varName) CreateLoadstringVariable()
        {
            string loadstringFuncHolderVar = _variableNamesGenerator.AllocVariable("__LOADSTRINGFUNC__");

            // This gives us _G[*var name*]
            // We will use it 2 times: 
            // 1. In the assignment of the literal "char" to it
            // 2. as an indexer to retrieve the "string.char" function 
            ElementAccessExpressionSyntax loadstringFuncAccessor = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, loadstringFuncHolderVar);
            var (loadstringNameCreationStatements, loadstringNameHolderVar) = GetStringLiteral("loadstring");

            // Construct this statment:
            // _G[FUNC_VAR_NAME] = _G[ _G[STR_VAR_NAME] ]
            // Which is the same as:
            // loadstring_ptr = _G["loadstring"]
            var loadstringFuncAssgn = SyntaxFactoryEx.AssignmentExpression(loadstringFuncAccessor,
                                        SyntaxFactory.ElementAccessExpression( // _G[FUNC_VAR_NAME] =
                                            SyntaxFactory.IdentifierName(_globalTableVar),    // _G[ $$$ ]
                                            FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, loadstringNameHolderVar))); // $$$ of line above --> _G[STR_VAR_NAME]

            // Final statements list:
            // 1. Assignment of the string variable to hold "loadstring" 
            // 2. Assignment of the func ptr variable to hold loadstring
            var statements = SyntaxFactory.StatementList(loadstringNameCreationStatements.Statements.Append(loadstringFuncAssgn));
            _debug_VarNameToExpectedValue[loadstringFuncHolderVar] = new LuaFunctionName("loadstring");

            return (statements, loadstringFuncHolderVar);
        }

        private void CreateBuildingBlocksStrings(List<StatementSyntax> exps)
        {
            // Assigning the first variable -- the one that holds the string of length 1 (It's "0")
            // The statment is "var_name = #_G .. [[]]"
            // It's a concatination of an empty string and the number '0' which result in a the "0" string
            _buildingBlocksStringVars[0] = _variableNamesGenerator.AllocVariable("__BBSTR__" + 0);
            var assgn_1 = SyntaxFactoryEx.AssignmentExpression(
                            FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, _buildingBlocksStringVars[0]),
                            SyntaxFactoryEx.ConcatStrings(
                                FuckedSyntaxFactory.FuckedZeroInteger(_globalTableVar),
                                FuckedSyntaxFactory.FuckedEmptyString()
                            ));
            exps.Add(assgn_1);
            _debug_VarNameToExpectedValue[_buildingBlocksStringVars[0]] = "0";


            for (int i = 1; i < this._buildingBlocksStringVars.Length; i++)
            {
                _buildingBlocksStringVars[i] = _variableNamesGenerator.AllocVariable("__BBSTR__" + i);

                var currVarAccess = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, _buildingBlocksStringVars[i]);
                var prevVarAccess = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, _buildingBlocksStringVars[i - 1]);

                // Compiling this expression:
                //      var_i = var_(i-1) .. var_(i-1)
                exps.Add(SyntaxFactoryEx.AssignmentExpression(
                            currVarAccess,
                            SyntaxFactoryEx.ConcatStrings(prevVarAccess, prevVarAccess)
                            ));
                _debug_VarNameToExpectedValue[_buildingBlocksStringVars[i]] = new string('0', (int)Math.Pow(2, i));
            }
        }

        public ElementAccessExpressionSyntax GetStringCharFunctionVariable()
        {
            return FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, _stringCharVar);
        }

        public (StatementListSyntax creationStatements, string varName) GetNumericLiteral(int number)
        {
            // TODO: Cache
            List<int> BreakDownToPowersOf2(int number)
            {
                List<int> output = new();
                int nextValue = 1;
                while (number != 0)
                {
                    if ((number & 0x1) == 0x1)
                    {
                        output.Add(nextValue);
                    }
                    number >>= 1;
                    nextValue *= 2;
                }
                return output;
            }

            if (number < 0)
            {
                throw new NotImplementedException("Can't generate negative numeric literals yet.");
            }


            List<int> componentsList = BreakDownToPowersOf2(number);
            string newVarName = _variableNamesGenerator.AllocVariable("__NUMLIT__" + number);
            var LeftSide = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, newVarName);
            // Now concatinating strings of length A,B,C,... where A,B,C,...∈ componentsList
            // For example for input=15, We get componentsList={8,4,2,1}
            // So we want to concat s1 .. s2 .. s4 .. s8 where sN is of length N
            // To make things simple we add a single empty string at the begining so we actually get
            // s0 .. s1 .. s2 .. s4 .. s8 for the last example
            // TODO: Migrate to #[[xxx...xxx]] (   e.g. 3 = #[[[[[]]    )
            ExpressionSyntax rightSide = FuckedSyntaxFactory.FuckedEmptyString();
            foreach (int i in componentsList)
            {
                // Getting the variable name allocated for a string of length 'i'
                // TODO: Boundries
                var powerOf2 = (int)Math.Log2(i);
                var stringVariableOfLengthI = _buildingBlocksStringVars[powerOf2];

                // Getting a Fucked Lua expression for accessing the found variable
                var expressionForVariableOfLengthI = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, stringVariableOfLengthI);
                rightSide = SyntaxFactoryEx.ConcatStrings(rightSide, expressionForVariableOfLengthI);
            }

            // Apply length operator to get the NUMBER from the string's length
            rightSide = SyntaxFactoryEx.LengthExpression(rightSide, autoParen: true);

            // Create assignment
            var assn = SyntaxFactoryEx.AssignmentExpression(LeftSide, rightSide);
            _debug_VarNameToExpectedValue[newVarName] = number;

            var statments = SyntaxFactory.StatementList(assn);

            return (statments, newVarName);
        }

        public (StatementListSyntax creationStatments, string varName) GetCharLiteral(char c)
        {
            if (!HasStringCharFunc)
            {
                throw new Exception("Can't generate CHAR literals without 'string.char' pointer");
            }
            // TODO: Cache

            // Creating a numeric literal with the ASCII value of the char
            byte asciiValue = (byte)c;
            var (numericalCreationStatements, numericalVarName) = GetNumericLiteral(asciiValue);

            // Getting accessing expression to the generated numeric literal
            var numericalVarAccessor = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, numericalVarName);

            // Get accessing expression to the pointer to the "string.char" function
            var stringCharFunctionVar = GetStringCharFunctionVariable();

            // Assigning variable for the character variable
            string newVarName = _variableNamesGenerator.AllocVariable("__CHARLIT__" + asciiValue);
            var LeftSide = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, newVarName);

            // Create assignment
            //  Right side: calling "string.char(NUMERIC_LITERAL)"
            var rightSide = SyntaxFactoryEx.FuncCall(stringCharFunctionVar, numericalVarAccessor);
            var assn = SyntaxFactoryEx.AssignmentExpression(LeftSide, rightSide);
            _debug_VarNameToExpectedValue[newVarName] = c;

            // To conclude: we need to return both the statements for creating the numeric literal + the single statment of declaring the char literal

            var statements = SyntaxFactory.StatementList(numericalCreationStatements.Statements.Append(assn));
            return (statements, newVarName);
        }


        /// <summary>
        /// Gets a string literal by using ".." for concatination
        /// </summary>
        public (StatementListSyntax creationStatments, string varName) GetStringLiteral(string literal)
        {
            List<StatementSyntax> charVarCreationStatments = new();

            // List of variables containing a variable name for every character of the string
            List<string> charVariables = new();

            foreach (char c in literal)
            {
                var (charStatements, charVarName) = GetCharLiteral(c);

                charVarCreationStatments.AddRange(charStatements.Statements);
                charVariables.Add(charVarName);
            }

            // Allocating variable for our string
            string newVarName = _variableNamesGenerator.AllocVariable("__STRLIT__" + MD5Helper.CalculateHash(literal)); // Generatnig unique identifier to all strings using MD5
            var LeftSide = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, newVarName);

            // Create assignment
            // Right side: concatinating "char1Var .. char2Var ..  <<...>> .. charNVar"
            // Starting with an empty string since it's easier that way, just appending to the end in every 'foreach' iteration
            ExpressionSyntax rightSide = FuckedSyntaxFactory.FuckedEmptyString();
            foreach (string currCharVar in charVariables)
            {
                var currCharAccessor = FuckedSyntaxFactory.FuckedVariableAccessExpression(_globalTableVar, currCharVar);
                rightSide = SyntaxFactoryEx.ConcatStrings(rightSide, currCharAccessor);
            }

            var assn = SyntaxFactoryEx.AssignmentExpression(LeftSide, rightSide);
            _debug_VarNameToExpectedValue[newVarName] = literal;

            // To conclude: we need to return all the statements for creating the char literals + the single statment of declaring the string literal
            var statements = SyntaxFactory.StatementList(charVarCreationStatments.Append(assn));
            return (statements, newVarName);
        }
    }
}
