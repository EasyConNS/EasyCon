namespace EasyScript;

public interface IOutputAdapter
{
    void Print(string message, bool newline);

    void Alert(string message);
}
