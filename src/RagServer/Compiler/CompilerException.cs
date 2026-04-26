namespace RagServer.Compiler;

/// <summary>Thrown by <see cref="IrToDslCompiler"/> when a QuerySpec cannot be compiled.</summary>
public sealed class CompilerException : Exception
{
    public CompilerException(string message) : base(message) { }
    public CompilerException(string message, Exception inner) : base(message, inner) { }
}
