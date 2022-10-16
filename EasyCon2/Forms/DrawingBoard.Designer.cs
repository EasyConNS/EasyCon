namespace EasyCon2.Forms
{
    partial class DrawingBoard
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.loadPicButton = new System.Windows.Forms.Button();
            this.startButton = new System.Windows.Forms.Button();
            this.stopButton = new System.Windows.Forms.Button();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.tipLabel = new System.Windows.Forms.Label();
            this.delayBox = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.durationBox = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.widthBox = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.heightBox = new System.Windows.Forms.TextBox();
            this.reverseCheck = new System.Windows.Forms.CheckBox();
            this.evaluateButton = new System.Windows.Forms.Button();
            this.evaluateLabel = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.startPosY = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.startPosX = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.curPosLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // loadPicButton
            // 
            this.loadPicButton.Location = new System.Drawing.Point(14, 277);
            this.loadPicButton.Name = "loadPicButton";
            this.loadPicButton.Size = new System.Drawing.Size(97, 50);
            this.loadPicButton.TabIndex = 0;
            this.loadPicButton.Text = "加载图片";
            this.loadPicButton.UseVisualStyleBackColor = true;
            this.loadPicButton.Click += new System.EventHandler(this.loadPicButton_Click);
            // 
            // startButton
            // 
            this.startButton.Location = new System.Drawing.Point(476, 274);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(64, 53);
            this.startButton.TabIndex = 1;
            this.startButton.Text = "开始画图";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.startButton_Click);
            // 
            // stopButton
            // 
            this.stopButton.Location = new System.Drawing.Point(546, 274);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(43, 52);
            this.stopButton.TabIndex = 2;
            this.stopButton.Text = "停止";
            this.stopButton.UseVisualStyleBackColor = true;
            this.stopButton.Click += new System.EventHandler(this.stopButton_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pictureBox1.Location = new System.Drawing.Point(17, 12);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(640, 240);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 3;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseDown);
            // 
            // tipLabel
            // 
            this.tipLabel.AutoEllipsis = true;
            this.tipLabel.Location = new System.Drawing.Point(10, 343);
            this.tipLabel.Name = "tipLabel";
            this.tipLabel.Size = new System.Drawing.Size(586, 17);
            this.tipLabel.TabIndex = 4;
            this.tipLabel.Text = "开始前，请先进入画画页面，笔刷调整到最小，光标移动到左上角，绘制是从左上到右下";
            // 
            // delayBox
            // 
            this.delayBox.Location = new System.Drawing.Point(426, 303);
            this.delayBox.Name = "delayBox";
            this.delayBox.Size = new System.Drawing.Size(44, 23);
            this.delayBox.TabIndex = 5;
            this.delayBox.Text = "45";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(388, 306);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(32, 17);
            this.label2.TabIndex = 6;
            this.label2.Text = "延迟";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(364, 277);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(56, 17);
            this.label3.TabIndex = 8;
            this.label3.Text = "按键持续";
            // 
            // durationBox
            // 
            this.durationBox.Location = new System.Drawing.Point(426, 274);
            this.durationBox.Name = "durationBox";
            this.durationBox.Size = new System.Drawing.Size(44, 23);
            this.durationBox.TabIndex = 7;
            this.durationBox.Text = "50";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(284, 277);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(32, 17);
            this.label4.TabIndex = 12;
            this.label4.Text = "列数";
            // 
            // widthBox
            // 
            this.widthBox.Location = new System.Drawing.Point(317, 274);
            this.widthBox.Name = "widthBox";
            this.widthBox.Size = new System.Drawing.Size(44, 23);
            this.widthBox.TabIndex = 11;
            this.widthBox.Text = "320";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(283, 306);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(32, 17);
            this.label5.TabIndex = 10;
            this.label5.Text = "行数";
            // 
            // heightBox
            // 
            this.heightBox.Location = new System.Drawing.Point(317, 303);
            this.heightBox.Name = "heightBox";
            this.heightBox.Size = new System.Drawing.Size(44, 23);
            this.heightBox.TabIndex = 9;
            this.heightBox.Text = "120";
            // 
            // reverseCheck
            // 
            this.reverseCheck.AutoSize = true;
            this.reverseCheck.Location = new System.Drawing.Point(117, 306);
            this.reverseCheck.Name = "reverseCheck";
            this.reverseCheck.Size = new System.Drawing.Size(75, 21);
            this.reverseCheck.TabIndex = 13;
            this.reverseCheck.Text = "反转黑白";
            this.reverseCheck.UseVisualStyleBackColor = true;
            // 
            // evaluateButton
            // 
            this.evaluateButton.Location = new System.Drawing.Point(595, 274);
            this.evaluateButton.Name = "evaluateButton";
            this.evaluateButton.Size = new System.Drawing.Size(65, 52);
            this.evaluateButton.TabIndex = 14;
            this.evaluateButton.Text = "估算耗时";
            this.evaluateButton.UseVisualStyleBackColor = true;
            this.evaluateButton.Click += new System.EventHandler(this.evaluateButton_Click);
            // 
            // evaluateLabel
            // 
            this.evaluateLabel.AutoSize = true;
            this.evaluateLabel.Location = new System.Drawing.Point(553, 343);
            this.evaluateLabel.Name = "evaluateLabel";
            this.evaluateLabel.Size = new System.Drawing.Size(44, 17);
            this.evaluateLabel.TabIndex = 15;
            this.evaluateLabel.Text = "耗时：";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(43, 255);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(324, 17);
            this.label1.TabIndex = 19;
            this.label1.Text = "（左上角0，0，右下角320，120）绘制起点        绘制大小";
            // 
            // startPosY
            // 
            this.startPosY.Location = new System.Drawing.Point(235, 304);
            this.startPosY.Name = "startPosY";
            this.startPosY.Size = new System.Drawing.Size(44, 23);
            this.startPosY.TabIndex = 18;
            this.startPosY.Text = "0";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(188, 280);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(44, 17);
            this.label6.TabIndex = 17;
            this.label6.Text = "起始列";
            // 
            // startPosX
            // 
            this.startPosX.Location = new System.Drawing.Point(235, 277);
            this.startPosX.Name = "startPosX";
            this.startPosX.Size = new System.Drawing.Size(44, 23);
            this.startPosX.TabIndex = 16;
            this.startPosX.Text = "0";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(188, 307);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(44, 17);
            this.label7.TabIndex = 20;
            this.label7.Text = "起始行";
            // 
            // curPosLabel
            // 
            this.curPosLabel.AutoSize = true;
            this.curPosLabel.Location = new System.Drawing.Point(388, 254);
            this.curPosLabel.Name = "curPosLabel";
            this.curPosLabel.Size = new System.Drawing.Size(115, 17);
            this.curPosLabel.TabIndex = 21;
            this.curPosLabel.Text = "当前坐标：100，40";
            // 
            // DrawingBoard
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(674, 373);
            this.Controls.Add(this.curPosLabel);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.startPosY);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.startPosX);
            this.Controls.Add(this.evaluateLabel);
            this.Controls.Add(this.evaluateButton);
            this.Controls.Add(this.reverseCheck);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.widthBox);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.heightBox);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.durationBox);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.delayBox);
            this.Controls.Add(this.tipLabel);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.stopButton);
            this.Controls.Add(this.startButton);
            this.Controls.Add(this.loadPicButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "DrawingBoard";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "画板";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Button loadPicButton;
        private Button startButton;
        private Button stopButton;
        private PictureBox pictureBox1;
        private Label tipLabel;
        private TextBox delayBox;
        private Label label2;
        private Label label3;
        private TextBox durationBox;
        private Label label4;
        private TextBox widthBox;
        private Label label5;
        private TextBox heightBox;
        private CheckBox reverseCheck;
        private Button evaluateButton;
        private Label evaluateLabel;
        private Label label1;
        private TextBox startPosY;
        private Label label6;
        private TextBox startPosX;
        private Label label7;
        private Label curPosLabel;
    }
}