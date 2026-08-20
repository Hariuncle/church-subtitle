using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Memo;

public class HangHelp : Form
{
	private IContainer components = null;

	private LinkLabel linkLabel1;

	private Label label1;

	private Label label2;

	private Button button1;

	public HangHelp()
	{
		InitializeComponent();
	}

	private void button1_Click(object sender, EventArgs e)
	{
		((Form)this).Close();
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
		linkLabel1 = new LinkLabel();
		label1 = new Label();
		label2 = new Label();
		button1 = new Button();
		((Control)this).SuspendLayout();
		((Control)linkLabel1).AutoSize = true;
		((Control)linkLabel1).Location = new Point(49, 31);
		((Control)linkLabel1).Name = "linkLabel1";
		((Control)linkLabel1).Size = new Size(186, 12);
		((Control)linkLabel1).TabIndex = 0;
		((Label)linkLabel1).TabStop = true;
		((Control)linkLabel1).Text = "dcv@daum.net";
		((Control)label1).AutoSize = true;
		((Control)label1).Location = new Point(11, 31);
		((Control)label1).Name = "label1";
		((Control)label1).Size = new Size(41, 12);
		((Control)label1).TabIndex = 1;
		((Control)label1).Text = "연락 : ";
		((Control)label2).AutoSize = true;
		((Control)label2).Location = new Point(12, 9);
		((Control)label2).Name = "label2";
		((Control)label2).Size = new Size(117, 12);
		((Control)label2).TabIndex = 2;
		((Control)label2).Text = "2020-04-03 David Kim";
		((Control)button1).Location = new Point(262, 10);
		((Control)button1).Name = "button1";
		((Control)button1).Size = new Size(47, 32);
		((Control)button1).TabIndex = 3;
		((Control)button1).Text = "확인";
		((ButtonBase)button1).UseVisualStyleBackColor = true;
		((Control)button1).Click += button1_Click;
		((ContainerControl)this).AutoScaleDimensions = new SizeF(7f, 12f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)1;
		((Form)this).ClientSize = new Size(316, 47);
		((Control)this).Controls.Add((Control)(object)button1);
		((Control)this).Controls.Add((Control)(object)label2);
		((Control)this).Controls.Add((Control)(object)label1);
		((Control)this).Controls.Add((Control)(object)linkLabel1);
		((Control)this).Name = "HangHelp";
		((Control)this).Text = "Information";
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}
}
