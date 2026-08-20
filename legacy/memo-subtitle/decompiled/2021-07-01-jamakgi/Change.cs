using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Memo;

public class Change : Form
{
	private RichTextBox rtb = null;

	private int pos = 0;

	private bool isFirst = true;

	private IContainer components = null;

	private Label findLbl;

	private Label changeLbl;

	private TextBox findTxt;

	private TextBox changeTxt;

	private Button changeBtn;

	private Button cancelBtn;

	public Change(RichTextBox sender)
	{
		rtb = sender;
		InitializeComponent();
	}

	private void Change_Load(object sender, EventArgs e)
	{
	}

	private void changeBtn_Click(object sender, EventArgs e)
	{
		if (isFirst)
		{
			isFirst = false;
			pos = rtb.Find(((Control)findTxt).Text, 0, (RichTextBoxFinds)0);
		}
		else
		{
			pos = rtb.Find(((Control)findTxt).Text, pos + 1, (RichTextBoxFinds)0);
		}
		if (pos >= 0)
		{
			((TextBoxBase)rtb).SelectedText = ((TextBoxBase)rtb).SelectedText.Replace(((Control)findTxt).Text, ((Control)changeTxt).Text);
		}
	}

	private void findTxt_TextChanged(object sender, EventArgs e)
	{
	}

	private void changeTxt_TextChanged(object sender, EventArgs e)
	{
	}

	private void cancelBtn_Click(object sender, EventArgs e)
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
		findLbl = new Label();
		changeLbl = new Label();
		findTxt = new TextBox();
		changeTxt = new TextBox();
		changeBtn = new Button();
		cancelBtn = new Button();
		((Control)this).SuspendLayout();
		((Control)findLbl).AutoSize = true;
		((Control)findLbl).Location = new Point(12, 9);
		((Control)findLbl).Name = "findLbl";
		((Control)findLbl).Size = new Size(88, 12);
		((Control)findLbl).TabIndex = 0;
		((Control)findLbl).Text = "찾을 내용(&N) : ";
		((Control)changeLbl).AutoSize = true;
		((Control)changeLbl).Location = new Point(12, 37);
		((Control)changeLbl).Name = "changeLbl";
		((Control)changeLbl).Size = new Size(87, 12);
		((Control)changeLbl).TabIndex = 1;
		((Control)changeLbl).Text = "바꿀 내용(&P) : ";
		((Control)findTxt).Location = new Point(106, 6);
		((Control)findTxt).Name = "findTxt";
		((Control)findTxt).Size = new Size(187, 21);
		((Control)findTxt).TabIndex = 2;
		((Control)findTxt).TextChanged += findTxt_TextChanged;
		((Control)changeTxt).Location = new Point(106, 34);
		((Control)changeTxt).Name = "changeTxt";
		((Control)changeTxt).Size = new Size(187, 21);
		((Control)changeTxt).TabIndex = 3;
		((Control)changeTxt).TextChanged += changeTxt_TextChanged;
		((Control)changeBtn).Location = new Point(299, 6);
		((Control)changeBtn).Name = "changeBtn";
		((Control)changeBtn).Size = new Size(82, 21);
		((Control)changeBtn).TabIndex = 4;
		((Control)changeBtn).Text = "바꾸기(&R)";
		((ButtonBase)changeBtn).UseVisualStyleBackColor = true;
		((Control)changeBtn).Click += changeBtn_Click;
		((Control)cancelBtn).Location = new Point(299, 34);
		((Control)cancelBtn).Name = "cancelBtn";
		((Control)cancelBtn).Size = new Size(82, 21);
		((Control)cancelBtn).TabIndex = 5;
		((Control)cancelBtn).Text = "취소";
		((ButtonBase)cancelBtn).UseVisualStyleBackColor = true;
		((Control)cancelBtn).Click += cancelBtn_Click;
		((ContainerControl)this).AutoScaleDimensions = new SizeF(7f, 12f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)1;
		((Form)this).ClientSize = new Size(396, 63);
		((Control)this).Controls.Add((Control)(object)cancelBtn);
		((Control)this).Controls.Add((Control)(object)changeBtn);
		((Control)this).Controls.Add((Control)(object)changeTxt);
		((Control)this).Controls.Add((Control)(object)findTxt);
		((Control)this).Controls.Add((Control)(object)changeLbl);
		((Control)this).Controls.Add((Control)(object)findLbl);
		((Control)this).Name = "Change";
		((Control)this).Text = "바꾸기";
		((Form)this).Load += Change_Load;
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}
}
