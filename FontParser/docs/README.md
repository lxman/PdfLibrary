# FontParser

A pure C# implementation of a parser to read TTF, OTF, TTC, OTC, WOFF and WOFF2 files and present their contents in logical order.

This is a ``dotnet standard 2.1`` library to make it useful in as many places as possible.

It is also published under an ``MIT license``.

To add this to your project use ``dotnet add package FontParser``

To use it, create a new ``FontReader`` object.

Then execute either the ``ReadFile(string file)`` or the ``ReadFileAsync(string file)`` method.

You will be returned a ``List<FontStructure>`` in which will be a hierarchical structure of the tables in the font(s).