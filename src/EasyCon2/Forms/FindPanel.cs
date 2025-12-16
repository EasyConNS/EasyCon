using ICSharpCode.AvalonEdit;
using System.Diagnostics;

namespace EasyCon2.Forms;

public partial class FindPanel : UserControl
{
    public FindPanel()
    {
        InitializeComponent();
    }

    public void InitEditor(TextEditor _editor)
    {
        editor = _editor;
    }

    private TextEditor editor;

    public string Target { get; set; }

    public string Replaced { get; set; }

    private void closeBtn_Click(object sender, EventArgs e)
    {
        Hide();
    }

    private void searchBtn_Click(object sender, EventArgs e)
    {
        Debug.WriteLine(Target);
        var index = Find();
        if (index == -1)
        {
            MessageBox.Show("到底了");
            return;
        }

        editor.Select(index, Target.Length);
        editor.ScrollToLine(editor.Document.GetLineByOffset(index).LineNumber);
    }

    private void replaceBtn_Click(object sender, EventArgs e)
    {
        if(editor.SelectedText == Target)
        {
            editor.SelectedText = Replaced;
        }
    }

    public int Find()
    {
        return editor.Text.IndexOf(Target, editor.SelectionStart+1); 
    }
}
