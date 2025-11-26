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
            loadPicButton = new Button();
            startButton = new Button();
            stopButton = new Button();
            pictureBox1 = new PictureBox();
            tipLabel = new Label();
            delayBox = new TextBox();
            label2 = new Label();
            label3 = new Label();
            durationBox = new TextBox();
            label4 = new Label();
            widthBox = new TextBox();
            label5 = new Label();
            heightBox = new TextBox();
            reverseCheck = new CheckBox();
            evaluateButton = new Button();
            evaluateLabel = new Label();
            label1 = new Label();
            startPosY = new TextBox();
            label6 = new Label();
            startPosX = new TextBox();
            label7 = new Label();
            curPosLabel = new Label();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // loadPicButton
            // 
            loadPicButton.Location = new Point(18, 326);
            loadPicButton.Margin = new Padding(4, 4, 4, 4);
            loadPicButton.Name = "loadPicButton";
            loadPicButton.Size = new Size(125, 59);
            loadPicButton.TabIndex = 0;
            loadPicButton.Text = "加载图片";
            loadPicButton.UseVisualStyleBackColor = true;
            loadPicButton.Click += loadPicButton_Click;
            // 
            // startButton
            // 
            startButton.Location = new Point(612, 322);
            startButton.Margin = new Padding(4, 4, 4, 4);
            startButton.Name = "startButton";
            startButton.Size = new Size(82, 62);
            startButton.TabIndex = 1;
            startButton.Text = "开始画图";
            startButton.UseVisualStyleBackColor = true;
            startButton.Click += startButton_Click;
            // 
            // stopButton
            // 
            stopButton.Location = new Point(702, 322);
            stopButton.Margin = new Padding(4, 4, 4, 4);
            stopButton.Name = "stopButton";
            stopButton.Size = new Size(55, 61);
            stopButton.TabIndex = 2;
            stopButton.Text = "停止";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += stopButton_Click;
            // 
            // pictureBox1
            // 
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.Location = new Point(22, 14);
            pictureBox1.Margin = new Padding(4, 4, 4, 4);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(822, 282);
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 3;
            pictureBox1.TabStop = false;
            pictureBox1.MouseDown += pictureBox1_MouseDown;
            // 
            // tipLabel
            // 
            tipLabel.AutoEllipsis = true;
            tipLabel.Location = new Point(13, 404);
            tipLabel.Margin = new Padding(4, 0, 4, 0);
            tipLabel.Name = "tipLabel";
            tipLabel.Size = new Size(753, 20);
            tipLabel.TabIndex = 4;
            tipLabel.Text = "开始前，请先进入画画页面，笔刷调整到最小，光标移动到左上角，绘制是从左上到右下";
            // 
            // delayBox
            // 
            delayBox.Location = new Point(548, 356);
            delayBox.Margin = new Padding(4, 4, 4, 4);
            delayBox.Name = "delayBox";
            delayBox.Size = new Size(55, 27);
            delayBox.TabIndex = 5;
            delayBox.Text = "45";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(499, 360);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(39, 20);
            label2.TabIndex = 6;
            label2.Text = "延迟";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(468, 326);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new Size(69, 20);
            label3.TabIndex = 8;
            label3.Text = "按键持续";
            // 
            // durationBox
            // 
            durationBox.Location = new Point(548, 322);
            durationBox.Margin = new Padding(4, 4, 4, 4);
            durationBox.Name = "durationBox";
            durationBox.Size = new Size(55, 27);
            durationBox.TabIndex = 7;
            durationBox.Text = "50";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(365, 326);
            label4.Margin = new Padding(4, 0, 4, 0);
            label4.Name = "label4";
            label4.Size = new Size(39, 20);
            label4.TabIndex = 12;
            label4.Text = "列数";
            // 
            // widthBox
            // 
            widthBox.Location = new Point(408, 322);
            widthBox.Margin = new Padding(4, 4, 4, 4);
            widthBox.Name = "widthBox";
            widthBox.Size = new Size(55, 27);
            widthBox.TabIndex = 11;
            widthBox.Text = "320";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(364, 360);
            label5.Margin = new Padding(4, 0, 4, 0);
            label5.Name = "label5";
            label5.Size = new Size(39, 20);
            label5.TabIndex = 10;
            label5.Text = "行数";
            // 
            // heightBox
            // 
            heightBox.Location = new Point(408, 356);
            heightBox.Margin = new Padding(4, 4, 4, 4);
            heightBox.Name = "heightBox";
            heightBox.Size = new Size(55, 27);
            heightBox.TabIndex = 9;
            heightBox.Text = "120";
            // 
            // reverseCheck
            // 
            reverseCheck.AutoSize = true;
            reverseCheck.Location = new Point(150, 360);
            reverseCheck.Margin = new Padding(4, 4, 4, 4);
            reverseCheck.Name = "reverseCheck";
            reverseCheck.Size = new Size(91, 24);
            reverseCheck.TabIndex = 13;
            reverseCheck.Text = "反转黑白";
            reverseCheck.UseVisualStyleBackColor = true;
            // 
            // evaluateButton
            // 
            evaluateButton.Location = new Point(765, 322);
            evaluateButton.Margin = new Padding(4, 4, 4, 4);
            evaluateButton.Name = "evaluateButton";
            evaluateButton.Size = new Size(84, 61);
            evaluateButton.TabIndex = 14;
            evaluateButton.Text = "估算耗时";
            evaluateButton.UseVisualStyleBackColor = true;
            evaluateButton.Click += evaluateButton_Click;
            // 
            // evaluateLabel
            // 
            evaluateLabel.AutoSize = true;
            evaluateLabel.Location = new Point(711, 404);
            evaluateLabel.Margin = new Padding(4, 0, 4, 0);
            evaluateLabel.Name = "evaluateLabel";
            evaluateLabel.Size = new Size(54, 20);
            evaluateLabel.TabIndex = 15;
            evaluateLabel.Text = "耗时：";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(55, 300);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(398, 20);
            label1.TabIndex = 19;
            label1.Text = "（左上角0，0，右下角320，120）绘制起点        绘制大小";
            // 
            // startPosY
            // 
            startPosY.Location = new Point(302, 358);
            startPosY.Margin = new Padding(4, 4, 4, 4);
            startPosY.Name = "startPosY";
            startPosY.Size = new Size(55, 27);
            startPosY.TabIndex = 18;
            startPosY.Text = "0";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(242, 329);
            label6.Margin = new Padding(4, 0, 4, 0);
            label6.Name = "label6";
            label6.Size = new Size(54, 20);
            label6.TabIndex = 17;
            label6.Text = "起始列";
            // 
            // startPosX
            // 
            startPosX.Location = new Point(302, 326);
            startPosX.Margin = new Padding(4, 4, 4, 4);
            startPosX.Name = "startPosX";
            startPosX.Size = new Size(55, 27);
            startPosX.TabIndex = 16;
            startPosX.Text = "0";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(242, 361);
            label7.Margin = new Padding(4, 0, 4, 0);
            label7.Name = "label7";
            label7.Size = new Size(54, 20);
            label7.TabIndex = 20;
            label7.Text = "起始行";
            // 
            // curPosLabel
            // 
            curPosLabel.AutoSize = true;
            curPosLabel.Location = new Point(499, 299);
            curPosLabel.Margin = new Padding(4, 0, 4, 0);
            curPosLabel.Name = "curPosLabel";
            curPosLabel.Size = new Size(144, 20);
            curPosLabel.TabIndex = 21;
            curPosLabel.Text = "当前坐标：100，40";
            // 
            // DrawingBoard
            // 
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(867, 439);
            Controls.Add(curPosLabel);
            Controls.Add(label7);
            Controls.Add(label1);
            Controls.Add(startPosY);
            Controls.Add(label6);
            Controls.Add(startPosX);
            Controls.Add(evaluateLabel);
            Controls.Add(evaluateButton);
            Controls.Add(reverseCheck);
            Controls.Add(label4);
            Controls.Add(widthBox);
            Controls.Add(label5);
            Controls.Add(heightBox);
            Controls.Add(label3);
            Controls.Add(durationBox);
            Controls.Add(label2);
            Controls.Add(delayBox);
            Controls.Add(tipLabel);
            Controls.Add(pictureBox1);
            Controls.Add(stopButton);
            Controls.Add(startButton);
            Controls.Add(loadPicButton);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Margin = new Padding(4, 4, 4, 4);
            Name = "DrawingBoard";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "画板";
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();

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