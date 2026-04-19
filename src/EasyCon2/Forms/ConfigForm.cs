using EasyCon.Core.Config;

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
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
