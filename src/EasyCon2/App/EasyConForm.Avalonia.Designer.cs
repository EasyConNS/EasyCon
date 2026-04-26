namespace EasyCon2.App;

partial class EasyConFormAvalonia
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        //
        // EasyConFormAvalonia
        //
        AutoScaleDimensions = new SizeF(9F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        KeyPreview = true;
        MinimumSize = new Size(800, 500);
        Size = new Size(910, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "伊机控 EasyCon";
    }
}
