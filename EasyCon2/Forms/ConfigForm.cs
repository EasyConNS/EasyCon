using EasyCon2.Global;

namespace EasyCon2.Forms
{
    public partial class ConfigForm : Form
    {
        public enum TokenType
        {
            pushplus,
            channel,
        }


        private readonly VControllerConfig Config;
        public string TokenString => TokenBox.Text;
        private TokenType tokenType;
        public ConfigForm(VControllerConfig cfg, TokenType type)
        {
            InitializeComponent();
            tokenType = type;
            Config = cfg;
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            if(tokenType == TokenType.pushplus)
            {
                TokenBox.Text = Config.AlertToken;
            }
            else
            {
                TokenBox.Text = Config.ChannelToken;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(TokenBox.Text == "")
            {
                MessageBox.Show("Token不能为空");
                return;
            }
            if(tokenType == TokenType.pushplus)
            {
                if (TokenBox.Text.Length != 32)
                {
                    MessageBox.Show("Token格式不正确，请仔细检查");
                    return;
                }
            }
            else
            {
                ;// todo some check
            }

            DialogResult = DialogResult.OK;
            Close();

        }
    }
}
