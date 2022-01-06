
namespace EasyCon2.Script
{
    public interface IOutputAdapter
    {
        void Print(string message, bool newline);

        void Alert(string message);
    }
}
