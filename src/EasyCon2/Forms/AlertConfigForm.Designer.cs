namespace EasyCon2.Forms;

partial class AlertConfigForm
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
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AlertConfigForm));
        SuspendLayout();
        // 
        // AlertConfigForm
        // 
        AutoScaleDimensions = new SizeF(9F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(542, 431);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(420, 400);
        Name = "AlertConfigForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "推送配置";
        ResumeLayout(false);
    }
}
