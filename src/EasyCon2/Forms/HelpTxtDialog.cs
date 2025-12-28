namespace EasyCon2.Forms
{
    public partial class HelpTxtDialog : Form
    {
        public HelpTxtDialog(string txt, string title = "帮助说明")
        {
            InitializeComponent();

            this.Text = title;
            textBox1.Text = txt;
        }
    }
}
