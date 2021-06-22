﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

/// <summary>
/// <see cref="QsFmt.Formatter.Tests.FixedPoint"/> test cases.
/// </summary>
module Microsoft.Quantum.QsFmt.Formatter.Tests.FixedPoints

open Antlr4.Runtime
open Microsoft.Quantum.QsFmt.Formatter
open Microsoft.Quantum.QsFmt.Formatter.Tests
open Microsoft.Quantum.QsFmt.Parser
open System.IO
open Xunit

/// <summary>
/// Returns true if <paramref name="source"/> is valid Q# syntax.
/// </summary>
let private isValidSyntax (source: string) =
    let parser = source |> AntlrInputStream |> QSharpLexer |> CommonTokenStream |> QSharpParser
    parser.document () |> ignore
    parser.NumberOfSyntaxErrors = 0

/// Test case files with valid syntax.
let private testCases () =
    Directory.GetFiles("TestCases", "*.qs", EnumerationOptions(RecurseSubdirectories = true))
    |> Seq.map File.ReadAllText
    |> Seq.filter isValidSyntax
    |> Seq.map (Array.create 1)

[<Theory(Skip = "Not supported.")>]
[<MemberData "testCases">]
let ``Identity preserves original source code`` source =
    Assert.Equal(Ok source |> ShowResult, Formatter.identity source |> ShowResult)

[<FixedPoint>]
let ``Namespace comments`` = """/// The Foo namespace.
namespace Foo {}

/// The Bar namespace.
namespace Bar {}

// End of file."""

[<FixedPoint>]
let ``Function with one parameter`` = """namespace Foo {
    function Bar(x : Int) : Int {
        return x;
    }
}"""

[<FixedPoint>]
let ``Function with two parameters`` = """namespace Foo {
    function Bar(x : Int, y : Int) : Int {
        return x + y;
    }
}"""

[<FixedPoint>]
let ``Operation with Adj characteristic`` = """namespace Foo {
    operation Bar () : Unit is Adj {}
}"""

[<FixedPoint>]
let ``Operation with Adj + Ctl characteristics`` = """namespace Foo {
    operation Bar () : Unit is Adj + Ctl {}
}"""

[<FixedPoint>]
let ``Entry point and using statement`` = """namespace Microsoft.Quantum.Foo {
    @EntryPoint()
    operation RunProgram (nQubits : Int) : Unit {
        using (register = Qubit[nQubits]) {
            H(register[0]);
        }
    }
}"""

[<FixedPoint>]
let ``Open directives and operation`` = """namespace Foo {
    open Bar;
    open Baz;

    operation Spam () : Unit {}
}"""

[<FixedPoint>]
let ``Open directive and entry point`` = """namespace Foo {
    open Test;

    @EntryPoint()
    operation Bar () : Unit {}
}"""

[<FixedPoint(Skip = "Not supported.")>]
let ``Operation with comments`` = """namespace Foo {
    open Test;

    operation Bar () : Unit {
        // This is a comment
        Message("Bar");
        // Lorem ipsum
        // Dolor sit amet
    }
}"""

[<FixedPoint>]
let ``Mutable variable`` = """namespace Foo {
    function Bar() : Unit {
        mutable x = 0;
    }
}"""

[<FixedPoint>]
let ``Mutable variable after comment`` = """namespace Foo {
    function Bar() : Unit {
        // Hello world
        mutable x = 0;
    }
}"""

[<FixedPoint(Skip = "Not supported.")>]
let ``Mutable variable before comment`` = """namespace Foo {
    function Bar() : Unit {
        mutable x = 0;
        // Hello world
    }
}"""

[<FixedPoint>]
let ``Array literal`` = """namespace Foo {
    function Bar() : Unit {
        let xs = [1, 2, 3];
    }
}"""

[<FixedPoint>]
let ``Declare type parameter`` = """namespace Foo {
    function Bar<'a>() : Unit {}
}
"""

[<FixedPoint>]
let ``Declare 2 type parameters`` = """namespace Foo {
    function Bar<'a, 'b>() : Unit {}
}
"""

[<FixedPoint>]
let ``Declare 3 type parameters`` = """namespace Foo {
    function Bar<'a, 'b, 'c>() : Unit {}
}
"""

[<FixedPoint>]
let ``Intrinsic body specialization`` = """namespace Foo {
    function Bar() : Unit {
        body intrinsic;
    }
}
"""

[<FixedPoint>]
let ``Intrinsic body and controlled specializations`` = """namespace Foo {
    function Bar() : Unit {
        body intrinsic;
        controlled intrinsic;
    }
}
"""

[<FixedPoint>]
let ``Explicit body and controlled specializations`` = """namespace Foo {
    function Bar() : Unit {
        body (...) {
            Message("body");
        }

        controlled (cs, ...) {
            Message("controlled");
        }
    }
}
"""
