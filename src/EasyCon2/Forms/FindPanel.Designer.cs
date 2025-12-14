namespace EasyCon2.Forms
{
    partial class FindPanel
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            searchBtn = new Button();
            searchTBox = new TextBox();
            bindingSource1 = new BindingSource(components);
            replaceTBox = new TextBox();
            replaceBtn = new Button();
            closeBtn = new Button();
            ((System.ComponentModel.ISupportInitialize)bindingSource1).BeginInit();
            SuspendLayout();
            // 
            // searchBtn
            // 
            searchBtn.Location = new Point(166, 29);
            searchBtn.Name = "searchBtn";
            searchBtn.Size = new Size(72, 35);
            searchBtn.TabIndex = 0;
            searchBtn.Text = "下一个";
            searchBtn.UseVisualStyleBackColor = true;
            searchBtn.Click += searchBtn_Click;
            // 
            // searchTBox
            // 
            searchTBox.DataBindings.Add(new Binding("Text", bindingSource1, "Target", true));
            searchTBox.Location = new Point(7, 33);
            searchTBox.Name = "searchTBox";
            searchTBox.Size = new Size(153, 27);
            searchTBox.TabIndex = 1;
            // 
            // bindingSource1
            // 
            bindingSource1.DataSource = this;
            bindingSource1.Position = 0;
            // 
            // replaceTBox
            // 
            replaceTBox.Location = new Point(7, 75);
            replaceTBox.Name = "replaceTBox";
            replaceTBox.Size = new Size(153, 27);
            replaceTBox.TabIndex = 2;
            // 
            // replaceBtn
            // 
            replaceBtn.Location = new Point(169, 74);
            replaceBtn.Name = "replaceBtn";
            replaceBtn.Size = new Size(69, 29);
            replaceBtn.TabIndex = 3;
            replaceBtn.Text = "替换";
            replaceBtn.UseVisualStyleBackColor = true;
            replaceBtn.Click += replaceBtn_Click;
            // 
            // closeBtn
            // 
            closeBtn.BackColor = SystemColors.Control;
            closeBtn.FlatStyle = FlatStyle.System;
            closeBtn.Location = new Point(219, -1);
            closeBtn.Name = "closeBtn";
            closeBtn.Size = new Size(27, 29);
            closeBtn.TabIndex = 4;
            closeBtn.Text = "X";
            closeBtn.UseVisualStyleBackColor = false;
            closeBtn.Click += closeBtn_Click;
            // 
            // FindPanel
            // 
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(closeBtn);
            Controls.Add(replaceBtn);
            Controls.Add(replaceTBox);
            Controls.Add(searchTBox);
            Controls.Add(searchBtn);
            Name = "FindPanel";
            Size = new Size(245, 106);
            ((System.ComponentModel.ISupportInitialize)bindingSource1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button searchBtn;
        private TextBox searchTBox;
        private TextBox replaceTBox;
        private Button replaceBtn;
        private Button closeBtn;
        private BindingSource bindingSource1;
    }
}
