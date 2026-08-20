using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Memo;

public class Find : Form
{
	private RichTextBox rtb = null;

	private int pos = 0;

	private bool isFirst = true;

	private IContainer components = null;

	private Label FindLbl;

	private TextBox FindTxt;

	private Button FindBtn;

	private Button CancelBtn;

	public Find(RichTextBox sender)
	{
		rtb = sender;
		InitializeComponent();
	}

	private void FindBtn_Click(object sender, EventArgs e)
	{
		if (isFirst)
		{
			isFirst = false;
			pos = rtb.Find(((Control)FindTxt).Text, 0, (RichTextBoxFinds)0);
		}
		else
		{
			pos = rtb.Find(((Control)FindTxt).Text, pos + 1, (RichTextBoxFinds)0);
		}
		if (pos >= 0)
		{
			((TextBoxBase)rtb).SelectionStart = pos;
			((Control)rtb).Focus();
		}
	}

	private void CancelBtn_Click(object sender, EventArgs e)
	{
		((Form)this).Close();
	}

	private void Find_Load(object sender, EventArgs e)
	{
	}

	private void FindTxt_TextChanged(object sender, EventArgs e)
	{
	}

	private void FindLbl_Click(object sender, EventArgs e)
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
		FindLbl = new Label();
		FindTxt = new TextBox();
		FindBtn = new Button();
		CancelBtn = new Button();
		((Control)this).SuspendLayout();
		((Control)FindLbl).AutoSize = true;
		((Control)FindLbl).Location = new Point(12, 18);
		((Control)FindLbl).Name = "FindLbl";
		((Control)FindLbl).Size = new Size(88, 12);
		((Control)FindLbl).TabIndex = 0;
		((Control)FindLbl).Text = "찾을 내용(&N) : ";
		((Control)FindLbl).Click += FindLbl_Click;
		((Control)FindTxt).Location = new Point(106, 12);
		((Control)FindTxt).Name = "FindTxt";
		((Control)FindTxt).Size = new Size(184, 21);
		((Control)FindTxt).TabIndex = 1;
		((Control)FindTxt).TextChanged += FindTxt_TextChanged;
		((Control)FindBtn).Location = new Point(106, 39);
		((Control)FindBtn).Name = "FindBtn";
		((Control)FindBtn).Size = new Size(79, 23);
		((Control)FindBtn).TabIndex = 3;
		((Control)FindBtn).Text = "찾기";
		((ButtonBase)FindBtn).UseVisualStyleBackColor = true;
		((Control)FindBtn).Click += FindBtn_Click;
		((Control)CancelBtn).Location = new Point(211, 39);
		((Control)CancelBtn).Name = "CancelBtn";
		((Control)CancelBtn).Size = new Size(79, 23);
		((Control)CancelBtn).TabIndex = 3;
		((Control)CancelBtn).Text = "취소";
		((ButtonBase)CancelBtn).UseVisualStyleBackColor = true;
		((Control)CancelBtn).Click += CancelBtn_Click;
		((ContainerControl)this).AutoScaleDimensions = new SizeF(7f, 12f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)1;
		((Form)this).ClientSize = new Size(302, 72);
		((Control)this).Controls.Add((Control)(object)CancelBtn);
		((Control)this).Controls.Add((Control)(object)FindBtn);
		((Control)this).Controls.Add((Control)(object)FindTxt);
		((Control)this).Controls.Add((Control)(object)FindLbl);
		((Form)this).FormBorderStyle = (FormBorderStyle)6;
		((Control)this).Name = "Find";
		((Control)this).Text = "찾기";
		((Form)this).Load += Find_Load;
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}
}
