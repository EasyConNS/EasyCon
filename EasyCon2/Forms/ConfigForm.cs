using EasyCon2.Global;

namespace EasyCon2.Forms
{
    public partial class ConfigForm : Form
    {
        private readonly VControllerConfig Config;
        public string TokenString => TokenBox.Text;
        public ConfigForm(VControllerConfig cfg)
        {
            InitializeComponent();

            Config = cfg;
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            TokenBox.Text = Config.AlertToken;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(TokenBox.Text == "")
            {
                MessageBox.Show("Token不能为空");
                return;
            }
            if (TokenBox.Text.Length != 32)
            {
                MessageBox.Show("Token格式不正确，请仔细检查");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
