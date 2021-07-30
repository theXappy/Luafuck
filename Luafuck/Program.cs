using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Luafuck
{
    class Program
    {
        static void Main(string[] args)
        {
            string originalFilePath = args.FirstOrDefault();
            if(originalFilePath == null)
            {
                Console.WriteLine("Missing command line argument: Lua script path");
                return;
            }

            var originalCode  = File.ReadAllText(originalFilePath);
            SyntaxTree tree = LuaSyntaxTree.ParseText(originalCode);

            // Good for debugging:
            //
            //StatementListSyntax topLevelStatementsSyntax = root.ChildNodes().Single() as StatementListSyntax;
            //List<SyntaxNode> topLevelStatmentsList = topLevelStatementsSyntax.ChildNodes().ToList();
            //

            ScriptObfuscator so = new();
            SyntaxTree obfusTree = so.Obfuscate(tree);

            Console.WriteLine(obfusTree.ToString());
        }
    }
}
