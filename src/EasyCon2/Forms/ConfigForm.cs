using EasyCon2.Config;

namespace EasyCon2.Forms
{
    public partial class ConfigForm : Form
    {
        public ConfigState Config => _config;
        private readonly ConfigState _config;

        public ConfigForm(ConfigState cfg)
        {
            InitializeComponent();

            _config = cfg;

            PPTokenBox.DataBindings.Add(new Binding("Text", _config, "AlertToken"));
            QQTokenBox.DataBindings.Add(new Binding("Text", _config, "ChannelToken"));
            PPCheckBox.DataBindings.Add(new Binding("Checked", _config, "EnableAlertToken"));
            QQCheckBox.DataBindings.Add(new Binding("Checked", _config, "EnableChannelToken"));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (_config.EnableAlertToken && !_config.CheckPPToken())
            {
                MessageBox.Show("pushplus推送Token格式不正确，请仔细检查");
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
