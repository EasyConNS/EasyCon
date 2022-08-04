using EasyCon2.Global;

namespace EasyCon2.Forms
{
    public partial class ConfigForm : Form
    {
        public VControllerConfig Config => _config;
        private readonly VControllerConfig _config;

        public ConfigForm(VControllerConfig cfg)
        {
            InitializeComponent();

            _config = cfg;

            PPTokenBox.DataBindings.Add(new Binding("Text", _config, "AlertToken"));
            QQTokenBox.DataBindings.Add(new Binding("Text", _config, "ChannelToken"));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(_config.AlertToken == "")
            {
                MessageBox.Show("pushlus推送Token不能为空");
                return;
            }
            if (_config.ChannelToken == "")
            {
                MessageBox.Show("频道推送Token不能为空");
                return;
            }
            if (_config.CheckPPToken())
            {
                MessageBox.Show("pushlus推送Token格式不正确，请仔细检查");
                return;
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
