using System.Diagnostics;

namespace EasyCon2.Forms;

public partial class FindPanel : UserControl
{
    public FindPanel()
    {
        InitializeComponent();
    }

    public string Target { get; set; }

    private void closeBtn_Click(object sender, EventArgs e)
    {
        Hide();
    }

    private void searchBtn_Click(object sender, EventArgs e)
    {
        Debug.WriteLine(Target);
    }

    private void replaceBtn_Click(object sender, EventArgs e)
    {

    }
}
