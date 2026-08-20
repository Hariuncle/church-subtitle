using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Memo.Properties;

namespace Memo;

public class MainForm : Form
{
	private Go GoForm;

	private Find FindForm;

	private Change ChangeForm;

	public TextBox tb;

	public int txtLine = 1;

	public int moveLine = 0;

	public char[] txt = null;

	public int cLength = 0;

	public string search = null;

	private Font printFont;

	private string streamToPrint = null;

	private bool SaveCheck = false;

	private bool EditText = false;

	private IContainer components = null;

	private MenuStrip menuStrip1;

	private ToolStripMenuItem menu_File;

	private ToolStripMenuItem menu_NewFile;

	private ToolStripMenuItem menu_OpenFile;

	private ToolStripMenuItem menu_SaveFile;

	private ToolStripMenuItem menu_AFile;

	private ToolStripSeparator toolStripSeparator1;

	private ToolStripMenuItem menu_PageFile;

	private ToolStripMenuItem menu_PrintFile;

	private ToolStripSeparator toolStripSeparator2;

	private ToolStripMenuItem menu_EndFile;

	private ToolStripMenuItem menu_Edit;

	private ToolStripMenuItem menu_UndoEdit;

	private ToolStripSeparator toolStripSeparator3;

	private ToolStripMenuItem menu_TEdit;

	private ToolStripMenuItem menu_CopyEdit;

	private ToolStripMenuItem menu_PasteEdit;

	private ToolStripMenuItem menu_DeleteEdit;

	private ToolStripSeparator toolStripSeparator4;

	private ToolStripMenuItem menu_FindEdit;

	private ToolStripMenuItem menu_NextEdit;

	private ToolStripMenuItem menu_Ori;

	private ToolStripMenuItem menu_View;

	private ToolStripMenuItem menu_Help;

	private ToolStripMenuItem menu_ReEdit;

	private ToolStripMenuItem menu_GoEdit;

	private ToolStripSeparator toolStripSeparator5;

	private ToolStripMenuItem menu_AllEdit;

	private ToolStripMenuItem menu_DateEdit;

	private OpenFileDialog openFileDialog1;

	private SaveFileDialog saveFileDialog1;

	private ToolStripMenuItem menu_WOri;

	private ToolStripMenuItem menu_FOri;

	private FontDialog fontDialog1;

	private ToolStripMenuItem menu_StatusView;

	private ToolStripMenuItem menu_ColorOri;

	private ToolStripMenuItem menu_HangHelp;

	private ToolStripSeparator toolStripSeparator6;

	private ToolStripMenuItem menu_JungHelp;

	private ColorDialog colorDialog1;

	private ToolStripMenuItem menu_RedoEdit;

	private ToolStripMenuItem menu_PlayView;

	private ToolStripButton 새로만들기NToolStripButton;

	private ToolStripButton 열기OToolStripButton;

	private ToolStripButton 저장SToolStripButton;

	private ToolStripButton 인쇄PToolStripButton;

	private ToolStripButton 잘라내기UToolStripButton;

	private ToolStripButton 복사CToolStripButton;

	private ToolStripButton 붙여넣기PToolStripButton;

	private ToolStripButton 도움말LToolStripButton;

	private RichTextBox TextBox;

	private PageSetupDialog pageSetupDialog1;

	private PrintDocument pd;

	private ToolStripStatusLabel Status;

	private StatusStrip statusStrip1;

	private ToolStripMenuItem 자막방송용입니다ToolStripMenuItem;

	private ToolStrip toolStrip1;

	private ToolStripMenuItem 자막방송용문자입력기입니다ToolStripMenuItem;

	public MainForm()
	{
		InitializeComponent();
	}

	public MainForm(int abc)
	{
		moveLine = abc;
		InitializeComponent();
	}

	private void MainForm_Load(object sender, EventArgs e)
	{
	}

	private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (!EditText)
		{
			((CancelEventArgs)(object)e).Cancel = true;
			return;
		}
		if (EditText)
		{
		}
		if (!EditText && (int)MessageBox.Show("저장", "나가기", (MessageBoxButtons)3) == 6)
		{
			if ((int)((CommonDialog)saveFileDialog1).ShowDialog() == 1)
			{
				SaveYorN();
			}
			else if ((int)((CommonDialog)saveFileDialog1).ShowDialog() == 7)
			{
				((Form)this).Close();
			}
		}
	}

	public void TextBox_TextChanged_1(object sender, EventArgs e)
	{
		EditText = true;
	}

	private void menu_File_Click(object sender, EventArgs e)
	{
	}

	private void menu_Edit_Click(object sender, EventArgs e)
	{
	}

	private void menu_Ori_Click(object sender, EventArgs e)
	{
	}

	private void menu_View_Click(object sender, EventArgs e)
	{
	}

	private void menu_Help_Click(object sender, EventArgs e)
	{
	}

	private void menu_NewFile_Click(object sender, EventArgs e)
	{
		try
		{
			if (((Control)TextBox).Text != "" && (int)MessageBox.Show((IWin32Window)(object)this, "작업중인 문서 저장?", "저장", (MessageBoxButtons)3) == 6 && (int)((CommonDialog)saveFileDialog1).ShowDialog() == 1)
			{
				SaveDocument(((FileDialog)saveFileDialog1).FileName);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("에러 : " + ex.Message, "Error");
		}
		finally
		{
			((TextBoxBase)TextBox).Clear();
		}
		SaveCheck = false;
		EditText = false;
	}

	private void menu_OpenFile_Click(object sender, EventArgs e)
	{
		try
		{
			if ((int)((CommonDialog)openFileDialog1).ShowDialog() == 1)
			{
				OpenDocument(((FileDialog)openFileDialog1).FileName);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("에러 : " + ex.Message, "Error");
		}
	}

	private void menu_SaveFile_Click(object sender, EventArgs e)
	{
		SaveYorN();
	}

	private void menu_AFile_Click(object sender, EventArgs e)
	{
		SaveYorN();
	}

	private void menu_PageFile_Click(object sender, EventArgs e)
	{
		PrintDocument val = new PrintDocument();
		val.DocumentName = ((Control)TextBox).Text;
		pageSetupDialog1.Document = val;
		((CommonDialog)pageSetupDialog1).ShowDialog();
	}

	private void menu_PrintFile_Click(object sender, EventArgs epp)
	{
		printFont = new Font("Arial", 10f);
		PrintDocument val = new PrintDocument();
		val.PrintPage += new PrintPageEventHandler(pd_PrintPage_1);
		val.Print();
	}

	private void menu_EndFile_Click(object sender, EventArgs e)
	{
		Application.Exit();
	}

	private void menu_UndoEdit_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Undo();
	}

	private void menu_RedoEdit_Click(object sender, EventArgs e)
	{
		TextBox.Redo();
	}

	private void menu_TEdit_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Cut();
	}

	private void menu_CopyEdit_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Copy();
	}

	private void menu_PasteEdit_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Paste();
	}

	private void menu_DeleteEdit_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Cut();
	}

	private void menu_FindEdit_Click(object sender, EventArgs e)
	{
		FindForm = new Find(TextBox);
		((Form)FindForm).Show((IWin32Window)(object)this);
	}

	private void menu_NextEdit_Click(object sender, EventArgs e)
	{
	}

	private void menu_ReEdit_Click(object sender, EventArgs e)
	{
		ChangeForm = new Change(TextBox);
		((Control)ChangeForm).Show();
	}

	private void menu_GoEdit_Click(object sender, EventArgs e)
	{
		GoForm = new Go(this);
		DialogResult val = ((Form)GoForm).ShowDialog();
		moveLine--;
		int num = 0;
		int textLength = ((TextBoxBase)TextBox).TextLength;
		char[] array = ((Control)TextBox).Text.ToCharArray();
		for (int i = 0; i < textLength; i++)
		{
			if (array[i].ToString().Equals("\n"))
			{
				num++;
				if (num == moveLine)
				{
					((TextBoxBase)TextBox).SelectionStart = i + 1;
					break;
				}
			}
		}
	}

	private void menu_AllEdit_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).SelectAll();
	}

	private void menu_DateEdit_Click(object sender, EventArgs e)
	{
		((Control)TextBox).Text = ((Control)TextBox).Text.Insert(((TextBoxBase)TextBox).SelectionStart, DateTime.Now.ToString());
	}

	private void menu_WOri_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).WordWrap = !((TextBoxBase)TextBox).WordWrap;
	}

	private void menu_FOri_Click(object sender, EventArgs e)
	{
		((CommonDialog)fontDialog1).ShowDialog();
		((Control)TextBox).Font = fontDialog1.Font;
	}

	private void menu_ColorOri_Click(object sender, EventArgs e)
	{
		((CommonDialog)colorDialog1).ShowDialog();
		TextBox.SelectionColor = colorDialog1.Color;
	}

	private void menu_StatusView_Click(object sender, EventArgs e)
	{
		((Control)statusStrip1).Visible = !((Control)statusStrip1).Visible;
	}

	private void menu_PlayView_Click(object sender, EventArgs e)
	{
		((Control)toolStrip1).Visible = !((Control)toolStrip1).Visible;
	}

	private void menu_HangHelp_Click(object sender, EventArgs e)
	{
		HangHelp hangHelp = new HangHelp();
		((Control)hangHelp).Show();
	}

	private void menu_JungHelp_Click(object sender, EventArgs e)
	{
	}

	private void 새로만들기NToolStripButton_Click(object sender, EventArgs e)
	{
		try
		{
			if (((Control)TextBox).Text != "" && (int)MessageBox.Show((IWin32Window)(object)this, "작업중인 문서 저장?", "저장", (MessageBoxButtons)3) == 6 && (int)((CommonDialog)saveFileDialog1).ShowDialog() == 1)
			{
				SaveDocument(((FileDialog)saveFileDialog1).FileName);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("에러 : " + ex.Message, "Error");
		}
		finally
		{
			((TextBoxBase)TextBox).Clear();
		}
	}

	private void 열기OToolStripButton_Click(object sender, EventArgs e)
	{
		try
		{
			if ((int)((CommonDialog)openFileDialog1).ShowDialog() == 1)
			{
				OpenDocument(((FileDialog)openFileDialog1).FileName);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("에러 : " + ex.Message, "Error");
		}
	}

	private void 저장SToolStripButton_Click(object sender, EventArgs e)
	{
		SaveYorN();
	}

	private void 인쇄PToolStripButton_Click(object sender, EventArgs e)
	{
		printFont = new Font("Arial", 10f);
		PrintDocument val = new PrintDocument();
		val.PrintPage += new PrintPageEventHandler(pd_PrintPage_1);
		val.Print();
	}

	private void AlignLeft_Click(object sender, EventArgs e)
	{
		TextBox.SelectionAlignment = (HorizontalAlignment)0;
	}

	private void AlignCenter_Click(object sender, EventArgs e)
	{
		TextBox.SelectionAlignment = (HorizontalAlignment)2;
	}

	private void AlignRight_Click(object sender, EventArgs e)
	{
		TextBox.SelectionAlignment = (HorizontalAlignment)1;
	}

	private void 잘라내기UToolStripButton_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Cut();
	}

	private void 복사CToolStripButton_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Copy();
	}

	private void 붙여넣기PToolStripButton_Click(object sender, EventArgs e)
	{
		((TextBoxBase)TextBox).Paste();
	}

	private void 도움말LToolStripButton_Click(object sender, EventArgs e)
	{
	}

	private bool SaveYorN()
	{
		try
		{
			if (!SaveCheck && (int)MessageBox.Show("저장", "메모장", (MessageBoxButtons)4) == 6 && (int)((CommonDialog)saveFileDialog1).ShowDialog() == 1)
			{
				SaveDocument(((FileDialog)saveFileDialog1).FileName);
				SaveCheck = true;
				EditText = false;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("bool SaveYorn Error" + ex.Source);
		}
		finally
		{
			((TextBoxBase)TextBox).SelectionStart = 0;
		}
		return false;
	}

	public void SaveDocument(string FileName)
	{
		StreamWriter streamWriter = new StreamWriter(FileName, append: false, Encoding.Default);
		streamWriter.Write(((Control)TextBox).Text);
		streamWriter.Close();
	}

	public void OpenDocument(string FileName)
	{
		try
		{
			StreamReader streamReader = new StreamReader(FileName, Encoding.Default);
			((Control)TextBox).Text = streamReader.ReadToEnd();
			((TextBoxBase)TextBox).SelectionStart = 0;
			streamReader.Close();
			SaveCheck = false;
		}
		catch (Exception ex)
		{
			MessageBox.Show("OpenDocument에서 에러 발생\n" + ex.Message + "\n" + ex.Source);
		}
	}

	private void pd_PrintPage_1(object sender, PrintPageEventArgs ev)
	{
		streamToPrint = ((Control)TextBox).Text;
		float num = 0f;
		float num2 = 0f;
		int num3 = 0;
		float num4 = ev.MarginBounds.Left;
		float num5 = ev.MarginBounds.Top;
		string text = null;
		if (streamToPrint == null)
		{
			MessageBox.Show("아하하하하하하");
		}
		MessageBox.Show(streamToPrint);
		num = (float)ev.MarginBounds.Height / printFont.GetHeight(ev.Graphics);
		try
		{
			num2 = num5 + (float)num3 * printFont.GetHeight(ev.Graphics);
			ev.Graphics.DrawString(streamToPrint, printFont, Brushes.Black, num4, num2, new StringFormat());
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message + "\n" + ex.InnerException);
		}
		if (text != null)
		{
			ev.HasMorePages = true;
		}
		else
		{
			ev.HasMorePages = false;
		}
	}

	private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
	{
	}

	private void toolStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
	{
	}

	private void statusStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
	{
	}

	private void toolStripStatusLabel1_Click(object sender, EventArgs e)
	{
	}

	private void saveFileDialog1_FileOk(object sender, CancelEventArgs e)
	{
	}

	private void 자막방송용입니다ToolStripMenuItem_Click(object sender, EventArgs e)
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
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(MainForm));
		menuStrip1 = new MenuStrip();
		menu_File = new ToolStripMenuItem();
		menu_NewFile = new ToolStripMenuItem();
		menu_OpenFile = new ToolStripMenuItem();
		menu_SaveFile = new ToolStripMenuItem();
		menu_AFile = new ToolStripMenuItem();
		toolStripSeparator1 = new ToolStripSeparator();
		menu_PageFile = new ToolStripMenuItem();
		menu_PrintFile = new ToolStripMenuItem();
		toolStripSeparator2 = new ToolStripSeparator();
		menu_EndFile = new ToolStripMenuItem();
		menu_Edit = new ToolStripMenuItem();
		menu_UndoEdit = new ToolStripMenuItem();
		menu_RedoEdit = new ToolStripMenuItem();
		toolStripSeparator3 = new ToolStripSeparator();
		menu_TEdit = new ToolStripMenuItem();
		menu_CopyEdit = new ToolStripMenuItem();
		menu_PasteEdit = new ToolStripMenuItem();
		menu_DeleteEdit = new ToolStripMenuItem();
		toolStripSeparator4 = new ToolStripSeparator();
		menu_FindEdit = new ToolStripMenuItem();
		menu_NextEdit = new ToolStripMenuItem();
		menu_ReEdit = new ToolStripMenuItem();
		menu_GoEdit = new ToolStripMenuItem();
		toolStripSeparator5 = new ToolStripSeparator();
		menu_AllEdit = new ToolStripMenuItem();
		menu_DateEdit = new ToolStripMenuItem();
		menu_Ori = new ToolStripMenuItem();
		menu_WOri = new ToolStripMenuItem();
		menu_FOri = new ToolStripMenuItem();
		menu_ColorOri = new ToolStripMenuItem();
		menu_View = new ToolStripMenuItem();
		menu_StatusView = new ToolStripMenuItem();
		menu_PlayView = new ToolStripMenuItem();
		menu_Help = new ToolStripMenuItem();
		menu_HangHelp = new ToolStripMenuItem();
		toolStripSeparator6 = new ToolStripSeparator();
		menu_JungHelp = new ToolStripMenuItem();
		자막방송용입니다ToolStripMenuItem = new ToolStripMenuItem();
		자막방송용문자입력기입니다ToolStripMenuItem = new ToolStripMenuItem();
		openFileDialog1 = new OpenFileDialog();
		saveFileDialog1 = new SaveFileDialog();
		fontDialog1 = new FontDialog();
		colorDialog1 = new ColorDialog();
		toolStrip1 = new ToolStrip();
		새로만들기NToolStripButton = new ToolStripButton();
		열기OToolStripButton = new ToolStripButton();
		저장SToolStripButton = new ToolStripButton();
		인쇄PToolStripButton = new ToolStripButton();
		잘라내기UToolStripButton = new ToolStripButton();
		복사CToolStripButton = new ToolStripButton();
		붙여넣기PToolStripButton = new ToolStripButton();
		도움말LToolStripButton = new ToolStripButton();
		TextBox = new RichTextBox();
		pageSetupDialog1 = new PageSetupDialog();
		pd = new PrintDocument();
		Status = new ToolStripStatusLabel();
		statusStrip1 = new StatusStrip();
		((Control)menuStrip1).SuspendLayout();
		((Control)toolStrip1).SuspendLayout();
		((Control)statusStrip1).SuspendLayout();
		((Control)this).SuspendLayout();
		((ToolStrip)menuStrip1).BackColor = Color.Black;
		((ToolStrip)menuStrip1).GripMargin = new Padding(0);
		((ToolStrip)menuStrip1).ImageScalingSize = new Size(20, 20);
		((ToolStrip)menuStrip1).Items.AddRange((ToolStripItem[])(object)new ToolStripItem[5]
		{
			(ToolStripItem)menu_File,
			(ToolStripItem)menu_Edit,
			(ToolStripItem)menu_Ori,
			(ToolStripItem)menu_View,
			(ToolStripItem)menu_Help
		});
		((Control)menuStrip1).Location = new Point(0, 0);
		((Control)menuStrip1).Name = "menuStrip1";
		((Control)menuStrip1).Padding = new Padding(0);
		((Control)menuStrip1).Size = new Size(1920, 24);
		((Control)menuStrip1).TabIndex = 0;
		((Control)menuStrip1).Text = "menuStrip1";
		((ToolStrip)menuStrip1).ItemClicked += new ToolStripItemClickedEventHandler(menuStrip1_ItemClicked);
		((ToolStripItem)menu_File).BackColor = Color.Black;
		((ToolStripDropDownItem)menu_File).DropDownItems.AddRange((ToolStripItem[])(object)new ToolStripItem[9]
		{
			(ToolStripItem)menu_NewFile,
			(ToolStripItem)menu_OpenFile,
			(ToolStripItem)menu_SaveFile,
			(ToolStripItem)menu_AFile,
			(ToolStripItem)toolStripSeparator1,
			(ToolStripItem)menu_PageFile,
			(ToolStripItem)menu_PrintFile,
			(ToolStripItem)toolStripSeparator2,
			(ToolStripItem)menu_EndFile
		});
		((ToolStripItem)menu_File).Name = "menu_File";
		((ToolStripItem)menu_File).Size = new Size(57, 24);
		((ToolStripItem)menu_File).Text = "파일(F)";
		((ToolStripItem)menu_File).Click += menu_File_Click;
		((ToolStripItem)menu_NewFile).BackColor = SystemColors.Control;
		((ToolStripItem)menu_NewFile).Image = (Image)(object)Resources.NewDocument;
		((ToolStripItem)menu_NewFile).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_NewFile).Name = "menu_NewFile";
		menu_NewFile.ShortcutKeys = (Keys)131150;
		((ToolStripItem)menu_NewFile).Size = new Size(240, 26);
		((ToolStripItem)menu_NewFile).Text = "새로 만들기(&N)";
		((ToolStripItem)menu_NewFile).Click += menu_NewFile_Click;
		((ToolStripItem)menu_OpenFile).Image = (Image)(object)Resources.Open;
		((ToolStripItem)menu_OpenFile).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_OpenFile).Name = "menu_OpenFile";
		menu_OpenFile.ShortcutKeys = (Keys)131151;
		((ToolStripItem)menu_OpenFile).Size = new Size(240, 26);
		((ToolStripItem)menu_OpenFile).Text = "열기(&O)";
		((ToolStripItem)menu_OpenFile).Click += menu_OpenFile_Click;
		((ToolStripItem)menu_SaveFile).Image = (Image)(object)Resources.Save;
		((ToolStripItem)menu_SaveFile).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_SaveFile).Name = "menu_SaveFile";
		menu_SaveFile.ShortcutKeys = (Keys)131155;
		((ToolStripItem)menu_SaveFile).Size = new Size(240, 26);
		((ToolStripItem)menu_SaveFile).Text = "저장(&S)";
		((ToolStripItem)menu_SaveFile).Click += menu_SaveFile_Click;
		((ToolStripItem)menu_AFile).Name = "menu_AFile";
		menu_AFile.ShortcutKeys = (Keys)131137;
		((ToolStripItem)menu_AFile).Size = new Size(240, 26);
		((ToolStripItem)menu_AFile).Text = "다른 이름으로 저장(&A)";
		((ToolStripItem)menu_AFile).Click += menu_AFile_Click;
		((ToolStripItem)toolStripSeparator1).Name = "toolStripSeparator1";
		((ToolStripItem)toolStripSeparator1).Size = new Size(237, 6);
		((ToolStripItem)menu_PageFile).Name = "menu_PageFile";
		menu_PageFile.ShortcutKeys = (Keys)131157;
		((ToolStripItem)menu_PageFile).Size = new Size(240, 26);
		((ToolStripItem)menu_PageFile).Text = "페이지 설정(&U)";
		((ToolStripItem)menu_PageFile).Click += menu_PageFile_Click;
		((ToolStripItem)menu_PrintFile).Name = "menu_PrintFile";
		menu_PrintFile.ShortcutKeys = (Keys)131152;
		((ToolStripItem)menu_PrintFile).Size = new Size(240, 26);
		((ToolStripItem)menu_PrintFile).Text = "인쇄(&P)";
		((ToolStripItem)menu_PrintFile).Click += menu_PrintFile_Click;
		((ToolStripItem)toolStripSeparator2).Name = "toolStripSeparator2";
		((ToolStripItem)toolStripSeparator2).Size = new Size(237, 6);
		((ToolStripItem)menu_EndFile).Name = "menu_EndFile";
		menu_EndFile.ShortcutKeys = (Keys)131160;
		((ToolStripItem)menu_EndFile).Size = new Size(240, 26);
		((ToolStripItem)menu_EndFile).Text = "끝내기(&X)";
		((ToolStripItem)menu_EndFile).Click += menu_EndFile_Click;
		((ToolStripDropDownItem)menu_Edit).DropDownItems.AddRange((ToolStripItem[])(object)new ToolStripItem[15]
		{
			(ToolStripItem)menu_UndoEdit,
			(ToolStripItem)menu_RedoEdit,
			(ToolStripItem)toolStripSeparator3,
			(ToolStripItem)menu_TEdit,
			(ToolStripItem)menu_CopyEdit,
			(ToolStripItem)menu_PasteEdit,
			(ToolStripItem)menu_DeleteEdit,
			(ToolStripItem)toolStripSeparator4,
			(ToolStripItem)menu_FindEdit,
			(ToolStripItem)menu_NextEdit,
			(ToolStripItem)menu_ReEdit,
			(ToolStripItem)menu_GoEdit,
			(ToolStripItem)toolStripSeparator5,
			(ToolStripItem)menu_AllEdit,
			(ToolStripItem)menu_DateEdit
		});
		((ToolStripItem)menu_Edit).Name = "menu_Edit";
		((ToolStripItem)menu_Edit).Size = new Size(57, 24);
		((ToolStripItem)menu_Edit).Text = "편집(&E)";
		((ToolStripItem)menu_Edit).Click += menu_Edit_Click;
		((ToolStripItem)menu_UndoEdit).Image = (Image)(object)Resources.Edit_Undo;
		((ToolStripItem)menu_UndoEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_UndoEdit).Name = "menu_UndoEdit";
		menu_UndoEdit.ShortcutKeys = (Keys)131157;
		((ToolStripItem)menu_UndoEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_UndoEdit).Text = "실행취소(&U)";
		((ToolStripItem)menu_UndoEdit).Click += menu_UndoEdit_Click;
		((ToolStripItem)menu_RedoEdit).Image = (Image)(object)Resources.Edit_Redo;
		((ToolStripItem)menu_RedoEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_RedoEdit).Name = "menu_RedoEdit";
		menu_RedoEdit.ShortcutKeys = (Keys)131154;
		((ToolStripItem)menu_RedoEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_RedoEdit).Text = "되돌리기(&R)";
		((ToolStripItem)menu_RedoEdit).Click += menu_RedoEdit_Click;
		((ToolStripItem)toolStripSeparator3).Name = "toolStripSeparator3";
		((ToolStripItem)toolStripSeparator3).Size = new Size(196, 6);
		((ToolStripItem)menu_TEdit).Image = (Image)(object)Resources.Cut;
		((ToolStripItem)menu_TEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_TEdit).Name = "menu_TEdit";
		menu_TEdit.ShortcutKeys = (Keys)131156;
		((ToolStripItem)menu_TEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_TEdit).Text = "잘라내기(&T)";
		((ToolStripItem)menu_TEdit).Click += menu_TEdit_Click;
		((ToolStripItem)menu_CopyEdit).Image = (Image)(object)Resources.Copy;
		((ToolStripItem)menu_CopyEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_CopyEdit).Name = "menu_CopyEdit";
		menu_CopyEdit.ShortcutKeys = (Keys)131139;
		((ToolStripItem)menu_CopyEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_CopyEdit).Text = "복사(&C)";
		((ToolStripItem)menu_CopyEdit).Click += menu_CopyEdit_Click;
		((ToolStripItem)menu_PasteEdit).Image = (Image)(object)Resources.Paste;
		((ToolStripItem)menu_PasteEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_PasteEdit).Name = "menu_PasteEdit";
		menu_PasteEdit.ShortcutKeys = (Keys)131158;
		((ToolStripItem)menu_PasteEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_PasteEdit).Text = "붙여넣기(&P)";
		((ToolStripItem)menu_PasteEdit).Click += menu_PasteEdit_Click;
		((ToolStripItem)menu_DeleteEdit).Image = (Image)(object)Resources.Cut;
		((ToolStripItem)menu_DeleteEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_DeleteEdit).Name = "menu_DeleteEdit";
		menu_DeleteEdit.ShortcutKeys = (Keys)46;
		((ToolStripItem)menu_DeleteEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_DeleteEdit).Text = "삭제(&L)";
		((ToolStripItem)menu_DeleteEdit).Click += menu_DeleteEdit_Click;
		((ToolStripItem)toolStripSeparator4).Name = "toolStripSeparator4";
		((ToolStripItem)toolStripSeparator4).Size = new Size(196, 6);
		((ToolStripItem)menu_FindEdit).Image = (Image)(object)Resources.Find;
		((ToolStripItem)menu_FindEdit).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_FindEdit).Name = "menu_FindEdit";
		menu_FindEdit.ShortcutKeys = (Keys)131142;
		((ToolStripItem)menu_FindEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_FindEdit).Text = "찾기(&F)";
		((ToolStripItem)menu_FindEdit).Click += menu_FindEdit_Click;
		((ToolStripItem)menu_NextEdit).Name = "menu_NextEdit";
		menu_NextEdit.ShortcutKeys = (Keys)114;
		((ToolStripItem)menu_NextEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_NextEdit).Text = "다음 찾기(&N)";
		((ToolStripItem)menu_NextEdit).Click += menu_NextEdit_Click;
		((ToolStripItem)menu_ReEdit).Name = "menu_ReEdit";
		menu_ReEdit.ShortcutKeys = (Keys)131144;
		((ToolStripItem)menu_ReEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_ReEdit).Text = "바꾸기(&R)";
		((ToolStripItem)menu_ReEdit).Click += menu_ReEdit_Click;
		((ToolStripItem)menu_GoEdit).Name = "menu_GoEdit";
		menu_GoEdit.ShortcutKeys = (Keys)131143;
		((ToolStripItem)menu_GoEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_GoEdit).Text = "이동(&G)";
		((ToolStripItem)menu_GoEdit).Click += menu_GoEdit_Click;
		((ToolStripItem)toolStripSeparator5).Name = "toolStripSeparator5";
		((ToolStripItem)toolStripSeparator5).Size = new Size(196, 6);
		((ToolStripItem)menu_AllEdit).Name = "menu_AllEdit";
		menu_AllEdit.ShortcutKeys = (Keys)131137;
		((ToolStripItem)menu_AllEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_AllEdit).Text = "모두선택(&A)";
		((ToolStripItem)menu_AllEdit).Click += menu_AllEdit_Click;
		((ToolStripItem)menu_DateEdit).Name = "menu_DateEdit";
		menu_DateEdit.ShortcutKeys = (Keys)131140;
		((ToolStripItem)menu_DateEdit).Size = new Size(199, 26);
		((ToolStripItem)menu_DateEdit).Text = "시간 / 날짜(&D)";
		((ToolStripItem)menu_DateEdit).Click += menu_DateEdit_Click;
		((ToolStripDropDownItem)menu_Ori).DropDownItems.AddRange((ToolStripItem[])(object)new ToolStripItem[3]
		{
			(ToolStripItem)menu_WOri,
			(ToolStripItem)menu_FOri,
			(ToolStripItem)menu_ColorOri
		});
		((ToolStripItem)menu_Ori).Name = "menu_Ori";
		((ToolStripItem)menu_Ori).Size = new Size(60, 24);
		((ToolStripItem)menu_Ori).Text = "서식(&O)";
		((ToolStripItem)menu_Ori).Click += menu_Ori_Click;
		menu_WOri.CheckOnClick = true;
		((ToolStripItem)menu_WOri).Name = "menu_WOri";
		((ToolStripItem)menu_WOri).Size = new Size(165, 26);
		((ToolStripItem)menu_WOri).Text = "자동 줄 바꿈(&W)";
		((ToolStripItem)menu_WOri).Click += menu_WOri_Click;
		((ToolStripItem)menu_FOri).Image = (Image)(object)Resources.Font;
		((ToolStripItem)menu_FOri).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_FOri).Name = "menu_FOri";
		((ToolStripItem)menu_FOri).Size = new Size(165, 26);
		((ToolStripItem)menu_FOri).Text = "글꼴(&F)";
		((ToolStripItem)menu_FOri).Click += menu_FOri_Click;
		((ToolStripItem)menu_ColorOri).Image = (Image)(object)Resources.ChooseColor;
		((ToolStripItem)menu_ColorOri).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)menu_ColorOri).Name = "menu_ColorOri";
		((ToolStripItem)menu_ColorOri).Size = new Size(165, 26);
		((ToolStripItem)menu_ColorOri).Text = "색상(&C)";
		((ToolStripItem)menu_ColorOri).Click += menu_ColorOri_Click;
		((ToolStripDropDownItem)menu_View).DropDownItems.AddRange((ToolStripItem[])(object)new ToolStripItem[2]
		{
			(ToolStripItem)menu_StatusView,
			(ToolStripItem)menu_PlayView
		});
		((ToolStripItem)menu_View).Name = "menu_View";
		((ToolStripItem)menu_View).Size = new Size(59, 24);
		((ToolStripItem)menu_View).Text = "보기(&V)";
		((ToolStripItem)menu_View).Click += menu_View_Click;
		menu_StatusView.Checked = true;
		menu_StatusView.CheckOnClick = true;
		menu_StatusView.CheckState = (CheckState)1;
		((ToolStripItem)menu_StatusView).Name = "menu_StatusView";
		((ToolStripItem)menu_StatusView).Size = new Size(153, 22);
		((ToolStripItem)menu_StatusView).Text = "상태 표시줄(&S)";
		((ToolStripItem)menu_StatusView).Click += menu_StatusView_Click;
		menu_PlayView.Checked = true;
		menu_PlayView.CheckOnClick = true;
		menu_PlayView.CheckState = (CheckState)1;
		((ToolStripItem)menu_PlayView).Name = "menu_PlayView";
		((ToolStripItem)menu_PlayView).Size = new Size(153, 22);
		((ToolStripItem)menu_PlayView).Text = "실행 아이콘(&P)";
		((ToolStripItem)menu_PlayView).Click += menu_PlayView_Click;
		((ToolStripDropDownItem)menu_Help).DropDownItems.AddRange((ToolStripItem[])(object)new ToolStripItem[3]
		{
			(ToolStripItem)menu_HangHelp,
			(ToolStripItem)toolStripSeparator6,
			(ToolStripItem)menu_JungHelp
		});
		((ToolStripItem)menu_Help).Name = "menu_Help";
		((ToolStripItem)menu_Help).Size = new Size(72, 24);
		((ToolStripItem)menu_Help).Text = "도움말(&H)";
		((ToolStripItem)menu_Help).Click += menu_Help_Click;
		((ToolStripItem)menu_HangHelp).Name = "menu_HangHelp";
		((ToolStripItem)menu_HangHelp).Size = new Size(166, 22);
		((ToolStripItem)menu_HangHelp).Text = "제작자 정보(&H)";
		((ToolStripItem)menu_HangHelp).Click += menu_HangHelp_Click;
		((ToolStripItem)toolStripSeparator6).Name = "toolStripSeparator6";
		((ToolStripItem)toolStripSeparator6).Size = new Size(163, 6);
		((ToolStripDropDownItem)menu_JungHelp).DropDownItems.AddRange((ToolStripItem[])(object)new ToolStripItem[2]
		{
			(ToolStripItem)자막방송용입니다ToolStripMenuItem,
			(ToolStripItem)자막방송용문자입력기입니다ToolStripMenuItem
		});
		((ToolStripItem)menu_JungHelp).Name = "menu_JungHelp";
		((ToolStripItem)menu_JungHelp).Size = new Size(166, 22);
		((ToolStripItem)menu_JungHelp).Text = "캡션보드 정보(&A)";
		((ToolStripItem)menu_JungHelp).Click += menu_JungHelp_Click;
		((ToolStripItem)자막방송용입니다ToolStripMenuItem).Name = "자막방송용입니다ToolStripMenuItem";
		((ToolStripItem)자막방송용입니다ToolStripMenuItem).Size = new Size(238, 22);
		((ToolStripItem)자막방송용입니다ToolStripMenuItem).Text = "충현교회 에바다부에서 만든";
		((ToolStripItem)자막방송용입니다ToolStripMenuItem).Click += 자막방송용입니다ToolStripMenuItem_Click;
		((ToolStripItem)자막방송용문자입력기입니다ToolStripMenuItem).Name = "자막방송용문자입력기입니다ToolStripMenuItem";
		((ToolStripItem)자막방송용문자입력기입니다ToolStripMenuItem).Size = new Size(238, 22);
		((ToolStripItem)자막방송용문자입력기입니다ToolStripMenuItem).Text = "자막방송용 문자 입력기입니다";
		((FileDialog)openFileDialog1).FileName = "openFileDialog1";
		((FileDialog)openFileDialog1).Filter = "텍스트 파일(*.txt) | *.txt | 모든 파일(*.*) | *.*";
		((FileDialog)saveFileDialog1).Filter = "텍스트 파일(*.txt) | *.txt | 모든 파일(*.*) | *.*";
		((FileDialog)saveFileDialog1).FileOk += saveFileDialog1_FileOk;
		toolStrip1.BackColor = Color.Black;
		toolStrip1.GripStyle = (ToolStripGripStyle)0;
		toolStrip1.ImageScalingSize = new Size(20, 20);
		toolStrip1.Items.AddRange((ToolStripItem[])(object)new ToolStripItem[8]
		{
			(ToolStripItem)새로만들기NToolStripButton,
			(ToolStripItem)열기OToolStripButton,
			(ToolStripItem)저장SToolStripButton,
			(ToolStripItem)인쇄PToolStripButton,
			(ToolStripItem)잘라내기UToolStripButton,
			(ToolStripItem)복사CToolStripButton,
			(ToolStripItem)붙여넣기PToolStripButton,
			(ToolStripItem)도움말LToolStripButton
		});
		toolStrip1.LayoutStyle = (ToolStripLayoutStyle)3;
		((Control)toolStrip1).Location = new Point(0, 24);
		((Control)toolStrip1).Name = "toolStrip1";
		((Control)toolStrip1).Padding = new Padding(0);
		toolStrip1.RenderMode = (ToolStripRenderMode)1;
		((Control)toolStrip1).Size = new Size(1920, 7);
		((Control)toolStrip1).TabIndex = 3;
		toolStrip1.ItemClicked += new ToolStripItemClickedEventHandler(toolStrip1_ItemClicked);
		((ToolStripItem)새로만들기NToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)새로만들기NToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)새로만들기NToolStripButton).Name = "새로만들기NToolStripButton";
		((ToolStripItem)새로만들기NToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)새로만들기NToolStripButton).Text = "새로 만들기(&N)";
		((ToolStripItem)새로만들기NToolStripButton).Click += 새로만들기NToolStripButton_Click;
		((ToolStripItem)열기OToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)열기OToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)열기OToolStripButton).Name = "열기OToolStripButton";
		((ToolStripItem)열기OToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)열기OToolStripButton).Text = "열기(&O)";
		((ToolStripItem)열기OToolStripButton).Click += 열기OToolStripButton_Click;
		((ToolStripItem)저장SToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)저장SToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)저장SToolStripButton).Name = "저장SToolStripButton";
		((ToolStripItem)저장SToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)저장SToolStripButton).Text = "저장(&S)";
		((ToolStripItem)저장SToolStripButton).Click += 저장SToolStripButton_Click;
		((ToolStripItem)인쇄PToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)인쇄PToolStripButton).ImageAlign = (ContentAlignment)16;
		((ToolStripItem)인쇄PToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)인쇄PToolStripButton).Name = "인쇄PToolStripButton";
		((ToolStripItem)인쇄PToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)인쇄PToolStripButton).Text = "Print the file.";
		((ToolStripItem)인쇄PToolStripButton).Click += 인쇄PToolStripButton_Click;
		((ToolStripItem)잘라내기UToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)잘라내기UToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)잘라내기UToolStripButton).Name = "잘라내기UToolStripButton";
		((ToolStripItem)잘라내기UToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)잘라내기UToolStripButton).Text = "잘라내기(&U)";
		((ToolStripItem)잘라내기UToolStripButton).Click += 잘라내기UToolStripButton_Click;
		((ToolStripItem)복사CToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)복사CToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)복사CToolStripButton).Name = "복사CToolStripButton";
		((ToolStripItem)복사CToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)복사CToolStripButton).Text = "복사(&C)";
		((ToolStripItem)복사CToolStripButton).Click += 복사CToolStripButton_Click;
		((ToolStripItem)붙여넣기PToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)붙여넣기PToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)붙여넣기PToolStripButton).Name = "붙여넣기PToolStripButton";
		((ToolStripItem)붙여넣기PToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)붙여넣기PToolStripButton).Text = "붙여넣기(&P)";
		((ToolStripItem)붙여넣기PToolStripButton).Click += 붙여넣기PToolStripButton_Click;
		((ToolStripItem)도움말LToolStripButton).DisplayStyle = (ToolStripItemDisplayStyle)2;
		((ToolStripItem)도움말LToolStripButton).ImageTransparentColor = Color.Magenta;
		((ToolStripItem)도움말LToolStripButton).Name = "도움말LToolStripButton";
		((ToolStripItem)도움말LToolStripButton).Size = new Size(23, 4);
		((ToolStripItem)도움말LToolStripButton).Text = "도움말(&L)";
		((ToolStripItem)도움말LToolStripButton).Click += 도움말LToolStripButton_Click;
		((Control)TextBox).Anchor = (AnchorStyles)14;
		((Control)TextBox).BackColor = Color.FromArgb(71, 71, 71);
		((TextBoxBase)TextBox).BorderStyle = (BorderStyle)0;
		((Control)TextBox).Font = new Font("나눔고딕 ExtraBold", 52f, (FontStyle)1, (GraphicsUnit)3, (byte)129);
		((Control)TextBox).ForeColor = Color.White;
		((Control)TextBox).Location = new Point(0, 869);
		((Control)TextBox).Margin = new Padding(0);
		((Control)TextBox).Name = "TextBox";
		TextBox.ScrollBars = (RichTextBoxScrollBars)0;
		((Control)TextBox).Size = new Size(1920, 211);
		((Control)TextBox).TabIndex = 4;
		((Control)TextBox).Text = "";
		((Control)TextBox).TextChanged += TextBox_TextChanged_1;
		pd.PrintPage += new PrintPageEventHandler(pd_PrintPage_1);
		((ToolStripItem)Status).ForeColor = Color.Silver;
		((ToolStripItem)Status).Name = "Status";
		((ToolStripItem)Status).Size = new Size(0, 17);
		((ToolStripItem)Status).Click += toolStripStatusLabel1_Click;
		((ToolStrip)statusStrip1).BackColor = Color.Black;
		((ToolStrip)statusStrip1).ImageScalingSize = new Size(20, 20);
		((ToolStrip)statusStrip1).Items.AddRange((ToolStripItem[])(object)new ToolStripItem[1] { (ToolStripItem)Status });
		((Control)statusStrip1).Location = new Point(0, 1058);
		((Control)statusStrip1).Name = "statusStrip1";
		((Control)statusStrip1).Size = new Size(1920, 22);
		((Control)statusStrip1).TabIndex = 2;
		((Control)statusStrip1).Text = "statusStrip1";
		((ToolStrip)statusStrip1).ItemClicked += new ToolStripItemClickedEventHandler(statusStrip1_ItemClicked);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)3;
		((Control)this).BackColor = Color.Black;
		((Control)this).BackgroundImageLayout = (ImageLayout)0;
		((Form)this).ClientSize = new Size(1920, 1080);
		((Form)this).ControlBox = false;
		((Control)this).Controls.Add((Control)(object)TextBox);
		((Control)this).Controls.Add((Control)(object)toolStrip1);
		((Control)this).Controls.Add((Control)(object)statusStrip1);
		((Control)this).Controls.Add((Control)(object)menuStrip1);
		((Control)this).Font = new Font("굴림", 9f, (FontStyle)0, (GraphicsUnit)3, (byte)0);
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).Icon = (Icon)componentResourceManager.GetObject("$this.Icon");
		((Form)this).Location = new Point(1921, 0);
		((Form)this).MainMenuStrip = menuStrip1;
		((Control)this).Name = "MainForm";
		((Form)this).ShowIcon = false;
		((Form)this).ShowInTaskbar = false;
		((Form)this).StartPosition = (FormStartPosition)0;
		((Form)this).FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
		((Form)this).Load += MainForm_Load;
		((Control)menuStrip1).ResumeLayout(false);
		((Control)menuStrip1).PerformLayout();
		((Control)toolStrip1).ResumeLayout(false);
		((Control)toolStrip1).PerformLayout();
		((Control)statusStrip1).ResumeLayout(false);
		((Control)statusStrip1).PerformLayout();
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}
}
