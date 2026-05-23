namespace EasyScript;

public interface IOutputAdapter
{
    void Print(string message, bool newline);

    void Alert(string message);
}

public delegate string OcrDelegate(int x, int y, int width, int height, string lang);
public delegate string? FrameDelegate(int x, int y, int width, int height);
public delegate string? RoiDelegate(string base64, int x, int y, int width, int height);
public delegate int LabelMatchDelegate(string labelName);