namespace EasyScript;

public interface IOutputAdapter
{
    void Print(string message, bool newline);

    void Alert(string message);
}

public interface IImageAdapter
{
    string OCR(int x, int y, int width, int height, string lang);
}