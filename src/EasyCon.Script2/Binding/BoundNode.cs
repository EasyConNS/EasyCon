namespace EasyCon.Script2.Binding;

internal abstract class BoundNode
{
    // public abstract BoundNodeKind Kind { get; }

    public override string ToString()
    {
        using (var writer = new StringWriter())
        {
            this.WriteTo(writer);
            return writer.ToString();
        }
    }
}
