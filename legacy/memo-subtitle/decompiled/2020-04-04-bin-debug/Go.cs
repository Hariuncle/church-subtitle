using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Memo;

public class Go : Form
{
	private MainForm f1;

	private IContainer components = null;

	private Label label1;

	private TextBox Gotxt;

	private Button GoBtn;

	private Button Cancel;

	public Go(MainForm frm)
	{
		InitializeComponent();
		f1 = frm;
	}

	private void Gotxt_TextChanged(object sender, EventArgs e)
	{
		string text = ((Control)Gotxt).Text;
		for (int i = 0; i < text.Length; i++)
		{
			if (!char.IsDigit(text, i))
			{
				MessageBox.Show("숫자만 입력 가능합니다.", "에러");
				((Control)Gotxt).Text = "";
				break;
			}
		}
	}

	private void GoBtn_Click(object sender, EventArgs e)
	{
		f1.moveLine = int.Parse(((Control)Gotxt).Text);
		((Form)this).Close();
	}

	private void Cancel_Click(object sender, EventArgs e)
	{
		((Form)this).Close();
	}

	private void Go_Load(object sender, EventArgs e)
	{
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		((Form)this).Dispose(disposing);
	}

	private void InitializeComponent()
	{
		label1 = new Label();
		Gotxt = new TextBox();
		GoBtn = new Button();
		Cancel = new Button();
		((Control)this).SuspendLayout();
		((Control)label1).AutoSize = true;
		((Control)label1).Location = new Point(12, 9);
		((Control)label1).Name = "label1";
		((Control)label1).Size = new Size(57, 12);
		((Control)label1).TabIndex = 0;
		((Control)label1).Text = "줄 번호 : ";
		((Control)Gotxt).Location = new Point(75, 6);
		((Control)Gotxt).Name = "Gotxt";
		((Control)Gotxt).Size = new Size(53, 21);
		((Control)Gotxt).TabIndex = 1;
		((Control)Gotxt).TextChanged += Gotxt_TextChanged;
		((Control)GoBtn).Location = new Point(134, 5);
		((Control)GoBtn).Name = "GoBtn";
		((Control)GoBtn).Size = new Size(44, 22);
		((Control)GoBtn).TabIndex = 2;
		((Control)GoBtn).Text = "이동";
		((ButtonBase)GoBtn).UseVisualStyleBackColor = true;
		((Control)GoBtn).Click += GoBtn_Click;
		((Control)Cancel).Location = new Point(184, 6);
		((Control)Cancel).Name = "Cancel";
		((Control)Cancel).Size = new Size(44, 22);
		((Control)Cancel).TabIndex = 3;
		((Control)Cancel).Text = "취소";
		((ButtonBase)Cancel).UseVisualStyleBackColor = true;
		((Control)Cancel).Click += Cancel_Click;
		((ContainerControl)this).AutoScaleDimensions = new SizeF(7f, 12f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)1;
		((Form)this).ClientSize = new Size(236, 31);
		((Control)this).Controls.Add((Control)(object)Cancel);
		((Control)this).Controls.Add((Control)(object)GoBtn);
		((Control)this).Controls.Add((Control)(object)Gotxt);
		((Control)this).Controls.Add((Control)(object)label1);
		((Control)this).Name = "Go";
		((Control)this).Text = "Go";
		((Form)this).Load += Go_Load;
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}
}
