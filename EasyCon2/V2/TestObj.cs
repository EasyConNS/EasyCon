#if lua
namespace EasyCon2
{
    internal class TestObj
    {

        public void Msg(object txt, object? title = null)
        {
            if (title == null)
            {

                MessageBox.Show(txt.ToString());
            }
            else
            {
                MessageBox.Show(txt.ToString(), title.ToString());
            }
        }

        public static void StaticMsg(object txt)
        {
            MessageBox.Show(txt.ToString());
        }

    }
}
#endif