namespace demo_ocr_label
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            button1 = new Button();
            overlayPictureBox1 = new OverlayPictureBox();
            textBox1 = new TextBox();
            label2 = new Label();
            pictureBox1 = new PictureBox();
            label3 = new Label();
            pictureBox2 = new PictureBox();
            label4 = new Label();
            numericThreshold = new NumericUpDown();
            label5 = new Label();
            label6 = new Label();
            comboBoxCameraList = new ComboBox();
            label1 = new Label();
            label7 = new Label();
            label8 = new Label();
            label9 = new Label();
            numericUpDown1 = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)overlayPictureBox1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericThreshold).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).BeginInit();
            SuspendLayout();
            // 
            // button1
            // 
            button1.BackColor = Color.LightGreen;
            button1.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 163);
            button1.Location = new Point(119, 22);
            button1.Name = "button1";
            button1.Size = new Size(98, 37);
            button1.TabIndex = 0;
            button1.Text = "Mở Camera";
            button1.UseVisualStyleBackColor = false;
            button1.Click += button1_Click;
            // 
            // overlayPictureBox1
            // 
            overlayPictureBox1.GuideBox = new Rectangle(0, 0, 0, 0);
            overlayPictureBox1.IsObjectDetected = false;
            overlayPictureBox1.Location = new Point(317, 23);
            overlayPictureBox1.Margin = new Padding(3, 2, 3, 2);
            overlayPictureBox1.Name = "overlayPictureBox1";
            overlayPictureBox1.Size = new Size(596, 312);
            overlayPictureBox1.TabIndex = 4;
            overlayPictureBox1.TabStop = false;
            overlayPictureBox1.Click += overlayPictureBox1_Click;
            // 
            // textBox1
            // 
            textBox1.Font = new Font("Segoe UI", 13F);
            textBox1.Location = new Point(619, 383);
            textBox1.Margin = new Padding(3, 2, 3, 2);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.ReadOnly = true;
            textBox1.Size = new Size(421, 158);
            textBox1.TabIndex = 6;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.BackColor = Color.Lime;
            label2.Font = new Font("Segoe UI", 11.25F, FontStyle.Bold, GraphicsUnit.Point, 163);
            label2.Location = new Point(760, 351);
            label2.Name = "label2";
            label2.Size = new Size(153, 20);
            label2.TabIndex = 7;
            label2.Text = "3. Văn bản trích xuất";
            // 
            // pictureBox1
            // 
            pictureBox1.Location = new Point(12, 383);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(265, 158);
            pictureBox1.TabIndex = 8;
            pictureBox1.TabStop = false;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.BackColor = Color.IndianRed;
            label3.Font = new Font("Segoe UI", 11.25F, FontStyle.Bold, GraphicsUnit.Point, 163);
            label3.Location = new Point(73, 351);
            label3.Name = "label3";
            label3.Size = new Size(137, 20);
            label3.TabIndex = 9;
            label3.Text = "1. Nhãn nhận diện";
            // 
            // pictureBox2
            // 
            pictureBox2.Location = new Point(317, 383);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(265, 158);
            pictureBox2.TabIndex = 10;
            pictureBox2.TabStop = false;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.BackColor = Color.Yellow;
            label4.Font = new Font("Segoe UI", 11.25F, FontStyle.Bold, GraphicsUnit.Point, 163);
            label4.Location = new Point(350, 351);
            label4.Name = "label4";
            label4.Size = new Size(189, 20);
            label4.TabIndex = 11;
            label4.Text = "2. Góc dưới bên trái Nhãn";
            label4.Click += label4_Click;
            // 
            // numericThreshold
            // 
            numericThreshold.Font = new Font("Segoe UI", 13F);
            numericThreshold.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numericThreshold.Location = new Point(10, 240);
            numericThreshold.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            numericThreshold.Name = "numericThreshold";
            numericThreshold.Size = new Size(65, 31);
            numericThreshold.TabIndex = 12;
            numericThreshold.TextAlign = HorizontalAlignment.Right;
            numericThreshold.Value = new decimal(new int[] { 180, 0, 0, 0 });
            numericThreshold.ValueChanged += numericUpDown1_ValueChanged;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.BackColor = SystemColors.ActiveBorder;
            label5.Font = new Font("Segoe UI", 11F);
            label5.Location = new Point(10, 218);
            label5.Name = "label5";
            label5.Size = new Size(223, 20);
            label5.TabIndex = 13;
            label5.Text = "Ngưỡng sáng nhận diện (0-255):";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Font = new Font("Segoe UI", 13F);
            label6.ForeColor = Color.Red;
            label6.Location = new Point(320, 26);
            label6.Name = "label6";
            label6.Size = new Size(60, 25);
            label6.TabIndex = 14;
            label6.Text = "FPS: 0";
            label6.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // comboBoxCameraList
            // 
            comboBoxCameraList.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxCameraList.Font = new Font("Segoe UI", 11F);
            comboBoxCameraList.FormattingEnabled = true;
            comboBoxCameraList.Location = new Point(10, 110);
            comboBoxCameraList.Margin = new Padding(3, 2, 3, 2);
            comboBoxCameraList.Name = "comboBoxCameraList";
            comboBoxCameraList.Size = new Size(207, 28);
            comboBoxCameraList.TabIndex = 15;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.BackColor = SystemColors.ActiveBorder;
            label1.Font = new Font("Segoe UI", 11F);
            label1.Location = new Point(10, 88);
            label1.Name = "label1";
            label1.Size = new Size(101, 20);
            label1.TabIndex = 16;
            label1.Text = "Chọn Camera:";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Font = new Font("Segoe UI", 13F);
            label7.ForeColor = Color.Red;
            label7.Location = new Point(320, 49);
            label7.Name = "label7";
            label7.Size = new Size(108, 25);
            label7.TabIndex = 17;
            label7.Text = "Accuracy: -1";
            label7.Visible = false;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Font = new Font("Segoe UI", 15.2000008F, FontStyle.Bold);
            label8.ForeColor = Color.LimeGreen;
            label8.Location = new Point(530, 28);
            label8.Name = "label8";
            label8.Size = new Size(231, 30);
            label8.TabIndex = 18;
            label8.Text = "TRÍCH THÀNH CÔNG!";
            label8.Visible = false;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.BackColor = SystemColors.ActiveBorder;
            label9.Font = new Font("Segoe UI", 11F);
            label9.Location = new Point(10, 152);
            label9.Name = "label9";
            label9.Size = new Size(186, 20);
            label9.TabIndex = 19;
            label9.Text = "Thời gian dừng (giây, >0s):";
            // 
            // numericUpDown1
            // 
            numericUpDown1.Font = new Font("Segoe UI", 13F);
            numericUpDown1.Location = new Point(10, 173);
            numericUpDown1.Margin = new Padding(3, 2, 3, 2);
            numericUpDown1.Name = "numericUpDown1";
            numericUpDown1.Size = new Size(63, 31);
            numericUpDown1.TabIndex = 20;
            numericUpDown1.TextAlign = HorizontalAlignment.Right;
            numericUpDown1.Value = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown1.ValueChanged += numericUpDown1_ValueChanged_1;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1121, 636);
            Controls.Add(numericUpDown1);
            Controls.Add(label9);
            Controls.Add(label8);
            Controls.Add(label7);
            Controls.Add(label1);
            Controls.Add(comboBoxCameraList);
            Controls.Add(label6);
            Controls.Add(label5);
            Controls.Add(numericThreshold);
            Controls.Add(label4);
            Controls.Add(pictureBox2);
            Controls.Add(label3);
            Controls.Add(pictureBox1);
            Controls.Add(label2);
            Controls.Add(textBox1);
            Controls.Add(overlayPictureBox1);
            Controls.Add(button1);
            Name = "Form1";
            Text = "Demo Label OCR";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)overlayPictureBox1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericThreshold).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button button1;
        private OverlayPictureBox overlayPictureBox1;
        private TextBox textBox1;
        private Label label2;
        private PictureBox pictureBox1;
        private Label label3;
        private PictureBox pictureBox2;
        private Label label4;
        private NumericUpDown numericThreshold;
        private Label label5;
        private Label label6;
        private ComboBox comboBoxCameraList;
        private Label label1;
        private Label label7;
        private Label label8;
        private Label label9;
        private NumericUpDown numericUpDown1;
    }
}
