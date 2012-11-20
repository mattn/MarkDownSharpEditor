﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Collections;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using anrControls;
using mshtml;


namespace MarkDownSharpEditor
{

	public partial class Form1 : Form
	{

		private bool fNoTitle = true;                         //無題のまま編集中かどうか
		private string MarkDownTextFilePath = "";             //編集中のMDファイルパス
		private string TemporaryHtmlFilePath = "";            //プレビュー用のテンポラリHTMLファイルパス
		private string SelectedCssFilePath = "";              //選択中のCSSファイルパス
		private Encoding EditingFileEncoding = Encoding.UTF8; //編集中MDファイルの文字エンコーディング

		private int BrowserCurrentPos = 0;                    //現在表示中のブラウザープレビューの表示位置

		// MarkdownSyntaxKeywordクラスを格納する配列
		private ArrayList MarkdownSyntaxKeywordAarray = new ArrayList();
		// Undoバッファを管理する
		int undoCount = 0;
		List<string> UndoBuffer = new List<string>();

		private bool fConstraintChange = true;	//更新状態の抑制

		//-----------------------------------
		//コンストラクタ
		//-----------------------------------
		public Form1()
		{
			InitializeComponent();

			this.richTextBox1.DragEnter += new System.Windows.Forms.DragEventHandler(this.richTextBox1_DragEnter);
			this.richTextBox1.DragDrop += new System.Windows.Forms.DragEventHandler(this.richTextBox1_DragDrop);

			//設定を読み込む
			MarkDownSharpEditor.AppSettings.Instance.ReadFromXMLFile();
			richTextBox1.AllowDrop = true;

		}

		//----------------------------------------------------------------------
		// フォームをロード
		//----------------------------------------------------------------------
		private void Form1_Load(object sender, EventArgs e)
		{

			var obj = MarkDownSharpEditor.AppSettings.Instance;

			//-----------------------------------
			//フォーム位置・サイズ
			//-----------------------------------
			this.Left = obj.FormPos.X;
			this.Top = obj.FormPos.Y;
			this.Width = obj.FormSize.Width;
			this.Height = obj.FormSize.Height;
			this.richTextBox1.Width = obj.richEditWidth;

			//メインメニュー表示
			this.menuViewToolBar.Checked = obj.fViewToolBar;
			this.toolStrip1.Visible = obj.fViewToolBar;
			this.menuViewStatusBar.Checked = obj.fViewStatusBar;
			this.statusStrip1.Visible = obj.fViewStatusBar;
			this.menuViewWidthEvenly.Checked = obj.fSplitBarWidthEvenly;

			//ブラウザープレビューまでの間隔
			if (obj.AutoBrowserPreviewInterval > 0)
			{
				timer1.Interval = obj.AutoBrowserPreviewInterval;
			}

			//-----------------------------------
			//RichEditBoxフォント
			FontConverter fc = new FontConverter();
			try { richTextBox1.Font = (Font)fc.ConvertFromString(obj.FontFormat); }
			catch { }
			//RichEditBoxフォントカラー
			richTextBox1.ForeColor = Color.FromArgb(obj.richEditForeColor);
			//ステータスバーに表示
			toolStripStatusLabelFontInfo.Text =
				richTextBox1.Font.Name + "," + richTextBox1.Font.Size.ToString() + "pt";

			//エディターのシンタックスハイライター設定の反映
			RefreshSyntaxHightlighterKeyword();	

			//-----------------------------------
			//選択中のエンコーディングを表示
			foreach (EncodingInfo ei in Encoding.GetEncodings())
			{
				if (ei.GetEncoding().IsBrowserDisplay == true)
				{
					if (ei.CodePage == obj.CodePageNumber)
					{
						toolStripStatusLabelHtmlEncoding.Text = ei.DisplayName;
						break;
					}
				}
			}
			
			//-----------------------------------
			//指定されたCSSファイル名を表示
			toolStripStatusLabelCssFileName.Text = "CSSファイル指定なし";

			if (obj.ArrayCssFileList.Count > 0)
			{
				string FilePath = (string)obj.ArrayCssFileList[0];
				if (File.Exists(FilePath) == true)
				{
					toolStripStatusLabelCssFileName.Text = Path.GetFileName(FilePath);
					SelectedCssFilePath = FilePath;
				}
			}

			//-----------------------------------
			//出力するHTMLエンコーディング表示
			if (obj.HtmlEncodingOption == 0)
			{
				// 編集中（RichEditBox側）のエンコーディング
				// デフォルト
				// 基本的にはテキストファイルが読み込まれたときに表示する
				toolStripStatusLabelHtmlEncoding.Text = EditingFileEncoding.EncodingName; 
			}
			else
			{
				//エンコーディングを指定する(&C)
				Encoding enc = Encoding.GetEncoding(obj.CodePageNumber);
				toolStripStatusLabelHtmlEncoding.Text = enc.EncodingName; 
			}

		}
		
		//----------------------------------------------------------------------
		// フォームを表示
		//----------------------------------------------------------------------
		private void Form1_Shown(object sender, EventArgs e)
		{

			string DirPath = MarkDownSharpEditor.AppSettings.GetAppDataLocalPath();

			ArrayList FileArray = new ArrayList();

			//TODO: 「新しいウィンドウで開く」="/new"などの引数も含まれるので、
			//       その辺りの処理も将来的に入れる。

			//コマンドラインでファイルが投げ込まれてきている
			string[] cmds = System.Environment.GetCommandLineArgs();
			for ( int i = 1; i < cmds.Count(); i++ )
			{
				if (File.Exists(cmds[i]) == true)
				{
					FileArray.Add(cmds[i]);	
				}
			}

			try
			{
				if (FileArray.Count > 1)
				{
					DialogResult ret = MessageBox.Show("複数のファイルが読み込まれました。\n" +
														"現在の設定内容でHTMLファイルへの一括変換を行いますか？",
					"問い合わせ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

					if (ret == DialogResult.Yes)
					{
						//一括でHTMLファイル出力
						BatchOutputToHtmlFiles((String[])FileArray.ToArray(typeof(string)));
						return;

					}
					else if (ret == DialogResult.Cancel)
					{
						//キャンセル
						return;
					}
					else
					{	//「いいえ」
						bool fOpen = false;
						foreach (string FilePath in FileArray)
						{
							//最初のファイルだけ、このウィンドウだけ開く
							if (fOpen == false)
							{
								OpenFile(FilePath);
								fOpen = true;
							}
							else
							{
								//他の複数ファイルは順次新しいウィンドウで開く
								System.Diagnostics.Process.Start(
									Application.ExecutablePath, string.Format("{0}", FilePath));
							}
						}
					}
				}
				else if ( FileArray.Count == 1 )
				{
					OpenFile((string)FileArray[0]);
				}
				else
				{
					//前に編集していたファイルがあればそれを開く
					if (MarkDownSharpEditor.AppSettings.Instance.fOpenEditFileBefore == true)
					{
						if (MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Count > 0)
						{
							AppHistory EditedFilePath = new AppHistory();
							EditedFilePath = (AppHistory)MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles[0];
							if (File.Exists(EditedFilePath.md) == true)
							{
								TemporaryHtmlFilePath = "";
								OpenFile(EditedFilePath.md);
								return;
							}
						}
					}
					if (MarkDownTextFilePath == "")
					{
						//無ければ「無題」ファイル
						OpenFile(CreateNoTitleFilePath());
					}
				}
			}
			finally
			{
				fConstraintChange = false;
				//エディター側のSyntaxHighlighter反映
				SyntaxHightlighterWidthRegex(0);
				richTextBox1.Modified = false;
				//フォームタイトル更新
				FormTextChange();
			}
		}

		//----------------------------------------------------------------------
		// フォームタイトルの表示（更新）
		//----------------------------------------------------------------------
		private void FormTextChange()
		{
			//string FileName = System.IO.Path.GetFileName(MarkDownTextFilePath);

			string FileName;

			if (fNoTitle == true)
			{
				FileName = "(無題)";
			}
			else
			{
				FileName = MarkDownTextFilePath;
			}

			if ( richTextBox1.Modified == true ){
				FileName = FileName + "(更新)";
			}
			this.Text = FileName + " - " + Application.ProductName;

		}

		//----------------------------------------------------------------------
		// RichEditBoxの内容が変更されたとき
		//----------------------------------------------------------------------
		private void richTextBox1_ModifiedChanged(object sender, EventArgs e)
		{
			FormTextChange();
			
			//PreviewToBrowser();
			if (timer1.Enabled == true)
			{
				return;
			}
			else
			{
				timer1.Enabled = true;
			}


		}

		//----------------------------------------------------------------------
		// RichTextBox選択範囲の変更
		//----------------------------------------------------------------------
		private void richTextBox1_SelectionChanged(object sender, EventArgs e)
		{
			
			if (fConstraintChange == true)
			{
				return;
			}
			//ブラウザーも随従してスクロール
			timer2.Enabled = true;

		}

		//----------------------------------------------------------------------
		// RichTextBox内容変更（シンタックス・ハイライター表示）
		//----------------------------------------------------------------------
		private void richTextBox1_TextChanged(object sender, EventArgs e)
		{

			if (fConstraintChange == true)
			{
				return;
			}

			//一行前からシンタックスハイライターを反映
			int pos = richTextBox1.GetLineFromCharIndex(richTextBox1.SelectionStart) - 1;
			if (pos < 0)
			{
				pos = 0;
			}
			SyntaxHightlighterWidthRegex();

			//ブラウザープレビュー
			if (MarkDownSharpEditor.AppSettings.Instance.fAutoBrowserPreview == true)
			{
				timer1.Enabled = true;
			}

		}

		//----------------------------------------------------------------------
		// RichTextBox Key Press
		//----------------------------------------------------------------------
		private void richTextBox1_KeyPress(object sender, KeyPressEventArgs e)
		{
			undoCount++;
			UndoBuffer.Add(richTextBox1.Text.ToString());

		}

		//----------------------------------------------------------------------
		// RichTextBox Key Down
		//----------------------------------------------------------------------
		private void richTextBox1_KeyDown(object sender, KeyEventArgs e)
		{
			//連続入力のときは反映回数を抑制する
			timer1.Enabled = false;
			timer2.Enabled = false;

		}

		//----------------------------------------------------------------------
		// RichTextBoxのフォントが変更されたとき（マウススクロールなど）
		//----------------------------------------------------------------------
		private void richTextBox1_MouseWheel(object sender, EventArgs e)
		{

		}

		//----------------------------------------------------------------------
		// RichTextBoxのスクロールバーイベント（webBrowserスクロール同期）
		//----------------------------------------------------------------------
		private void richTextBox1_VScroll(object sender, EventArgs e)
		{

			if (fConstraintChange == true)
			{
				return;
			}

			if (webBrowser1.Document != null)
			{
				//RichTextBoxの現在の左上座標の文字インデックスを取得して、
			　//その位置までのパラグラフ数（物理行数）を取得する
				int CharNum = richTextBox1.GetCharIndexFromPosition(new Point(0, 0));
				int LinesNum = GetParagraphCount(CharNum);

				int i = 0;
				foreach (HtmlElement elm in  webBrowser1.Document.Body.Children)
				{
					i++;
					if (elm.TagName == "ul")
					{
						foreach (HtmlElement elmLi in elm.Children)
						{
							i++;
							if (i == LinesNum)
							{
								break;
							}
						}
					}

					if (i == LinesNum)
					{
						BrowserCurrentPos = elm.OffsetRectangle.Top;
						if (BrowserCurrentPos < 0)
						{
							BrowserCurrentPos = 0;
						}
						webBrowser1.Document.Window.ScrollTo(new Point(0, BrowserCurrentPos));
						break;
					}
				}
			}
		
		}

		//----------------------------------------------------------------------
		//フォームを閉じる前イベント
		//----------------------------------------------------------------------
		private void Form1_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (richTextBox1.Modified == true)
			{
				DialogResult ret = MessageBox.Show("編集中のファイルがあります。保存してから終了しますか？",
				"問い合わせ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

				if (ret == DialogResult.Yes)
				{
					if (SaveToEditingFile() == true )
					{
						fNoTitle = false;
					}
					else{
						//キャンセルで抜けてきた
						e.Cancel = true;
						return;
					}

				}
				else if (ret == DialogResult.Cancel)
				{
					e.Cancel = true;
					return;
				}
			}

			fConstraintChange = true;

			//無題ファイルのまま編集しているのなら削除
			if (fNoTitle == true)
			{
				if (File.Exists(MarkDownTextFilePath) == true)
				{
					try
					{
						File.Delete(MarkDownTextFilePath);
					}
					catch
					{
					}
				}
			}

			//データバージョン
			System.Reflection.Assembly asmbly =	System.Reflection.Assembly.GetExecutingAssembly();
			System.Version ver = asmbly.GetName().Version;
			MarkDownSharpEditor.AppSettings.Instance.Version = ver.Major*1000+ver.Minor*100+ver.Build*10+ver.Revision;

			//フォーム位置・サイズ
			MarkDownSharpEditor.AppSettings.Instance.FormPos = new Point(this.Left, this.Top);
			MarkDownSharpEditor.AppSettings.Instance.FormSize = new Size(this.Width, this.Height);
			MarkDownSharpEditor.AppSettings.Instance.richEditWidth = this.richTextBox1.Width;
			FontConverter fc = new FontConverter();
			MarkDownSharpEditor.AppSettings.Instance.FontFormat = fc.ConvertToString(richTextBox1.Font);
			MarkDownSharpEditor.AppSettings.Instance.richEditForeColor = richTextBox1.ForeColor.ToArgb();

			//表示オプションなど
			MarkDownSharpEditor.AppSettings.Instance.fViewToolBar = this.menuViewToolBar.Checked;
			MarkDownSharpEditor.AppSettings.Instance.fViewStatusBar = this.menuViewStatusBar.Checked;
			MarkDownSharpEditor.AppSettings.Instance.fSplitBarWidthEvenly = this.menuViewWidthEvenly.Checked;

			if (File.Exists(MarkDownTextFilePath) == true)
			{
				//編集中のファイルパス
				foreach (AppHistory data in MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles)
				{
					if (data.md == MarkDownTextFilePath)
					{   //いったん削除して
						MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Remove(data);
						break;
					}
				}
				AppHistory HistroyData = new AppHistory();
				HistroyData.md = MarkDownTextFilePath;
				HistroyData.css = SelectedCssFilePath;
				MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Insert(0, HistroyData);	//先頭に挿入
			}

			//設定の保存
			MarkDownSharpEditor.AppSettings.Instance.SaveToXMLFile();
			//MarkDownSharpEditor.AppSettings.Instance.SaveToJsonFile();
			
			//ブラウザを空白にする
			webBrowser1.Navigate("about:blank");

		}

		//-----------------------------------
		//フォームを閉じる（終了）イベント
		//-----------------------------------
		private void Form1_FormClosed(object sender, FormClosedEventArgs e)
		{
			//テンポラリファイルの削除
			DeleteTemporaryHtmlFilePath();
		}

		//-----------------------------------
		//フォームのリサイズ
		//-----------------------------------
		private void Form1_ResizeEnd(object sender, EventArgs e)
		{
			//ソースウィンドウとビューウィンドウを均等にするか
			if (menuViewWidthEvenly.Checked == true)
			{
				this.richTextBox1.Width =
					(splitContainer1.Width - splitContainer1.SplitterWidth) / 2;
			}
		}

		//-----------------------------------
		// ファイルのドラッグエンター
		//-----------------------------------
		private void richTextBox1_DragEnter(object sender, System.Windows.Forms.DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop) == true)
			{
				e.Effect = DragDropEffects.Copy;
			}
			else
			{
				e.Effect = DragDropEffects.None;
			}
		}

		//-----------------------------------
		// TODO:ファイルのドラッグ＆ドロップ
		//-----------------------------------
		private void richTextBox1_DragDrop(object sender, System.Windows.Forms.DragEventArgs e)
		{
			string[] FileArray = (string[])e.Data.GetData(DataFormats.FileDrop, false);
			if (FileArray.Length > 1)
			{
				if (FileArray.Length > 0)
				{
					DialogResult ret = MessageBox.Show("複数のファイルが読み込まれました。\n" +
														"現在の設定内容でHTMLファイルへの一括変換を行いますか？\n"+
														"「いいえ」を選択するとそのまますべてのファイル開きます。",
					"問い合わせ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

					if (ret == DialogResult.Yes)
					{
						//一括でHTMLファイル出力
						BatchOutputToHtmlFiles(FileArray);
						return;

					}
					else if (ret == DialogResult.Cancel)
					{
						//キャンセル
						return;
					}
					else
					{	//「いいえ」
						bool fOpen = false;
						foreach (string FilePath in FileArray)
						{
							//最初のファイルだけ、このウィンドウだけ開く
							if (fOpen == false)
							{
								OpenFile(FilePath);
								fOpen = true;
							}
							else
							{
								//他の複数ファイルは順次新しいウィンドウで開く
								System.Diagnostics.Process.Start(
									Application.ExecutablePath, string.Format("{0}", FilePath));
							}
						}
					}
				}
				else
				{
					//前に編集していたファイルがあればそれを開く
					if (MarkDownSharpEditor.AppSettings.Instance.fOpenEditFileBefore == true)
					{
						if (MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Count > 0)
						{
							AppHistory EditedFilePath = new AppHistory();
							EditedFilePath = (AppHistory)MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles[0];

							if (File.Exists(EditedFilePath.md) == true)
							{
								OpenFile(EditedFilePath.md);
							}
						}
					}
				}
				fConstraintChange = false;
				//フォームタイトル更新
				FormTextChange();

				ArrayList ArrayFileList = new ArrayList();
				foreach (string FilePath in FileArray)
				{
					ArrayFileList.Add(FilePath);
				}

				BatchOutputToHtmlFiles((String[])ArrayFileList.ToArray(typeof(string)));

			}
			else if ( FileArray.Count() == 1 )
			{
				//ファイルが一個の場合は編集中のウィンドウで開く
				OpenFile(FileArray[0]);
			}
		}

		//----------------------------------------------------------------------
		// TODO: .mdファイルを開く（OpenFile）
		//----------------------------------------------------------------------
		private bool OpenFile(string FilePath)
		{
			if (richTextBox1.Modified == true)
			{
				DialogResult ret = MessageBox.Show(
					"編集中のファイルがあります。保存してか らファイルを開きますか？",
					"問い合わせ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

				if (ret == DialogResult.Yes)
				{
					if (SaveToEditingFile() == false)
					{
						//キャンセルで抜けてきた
						return (false);
					}
				}
				else if (ret == DialogResult.Cancel)
				{
					return (false);
				}

				//編集履歴に残す
				foreach (AppHistory data in MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles)
				{
					if (data.md == MarkDownTextFilePath)
					{   //いったん削除して
						MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Remove(data);
						break;
					}
				}
				AppHistory HistroyData = new AppHistory();
				HistroyData.md = MarkDownTextFilePath;
				HistroyData.css = SelectedCssFilePath;
				MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Insert(0, HistroyData);	//先頭に挿入
			}


			//ファイル名指定で来ている（ドラッグ＆ドロップされた等）
			if (File.Exists(FilePath) == true)
			{
			}
			else
			{
				//オープンダイアログ表示
				if (openFileDialog1.ShowDialog() == DialogResult.OK)
				{
					FilePath = openFileDialog1.FileName;
				}
				else
				{
					return (false);
				}
			}

			//テンポラリファイルがあればここで削除
			DeleteTemporaryHtmlFilePath();

			//テキストファイルを開く
			byte[] bs;
			using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
			{
				bs = new byte[fs.Length];
				fs.Read(bs, 0, bs.Length);
			}

			//文字コードを取得する
			EditingFileEncoding = GetCode(bs);

			//ステータスバーに表示
			toolStripStatusLabelTextEncoding.Text = EditingFileEncoding.EncodingName;

			//編集中のエンコーディングを使用する(&D)か
			if (MarkDownSharpEditor.AppSettings.Instance.HtmlEncodingOption == 0)
			{
				toolStripStatusLabelHtmlEncoding.Text = EditingFileEncoding.EncodingName;
			}

			fConstraintChange = true;
			MarkDownTextFilePath = FilePath;	//編集中のファイルパスとする
			fNoTitle = false;                   //無題フラグOFF

			//ペアとなるCSSファイルがあるか探索してあれば適用する
			foreach (AppHistory data in MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles)
			{
				if (data.md == MarkDownTextFilePath)
				{
					if (File.Exists(data.css) == true)
					{
						SelectedCssFilePath = data.css;
						break;
					}
				}

			}

			//選択中のCSSファイル名をステータスバーに表示
			if (File.Exists(SelectedCssFilePath) == true)
			{
				toolStripStatusLabelCssFileName.Text = Path.GetFileName(SelectedCssFilePath);
			}
			else
			{
				toolStripStatusLabelCssFileName.Text = "CSSファイル指定なし";
			}

			//RichEditBoxに表示
			fConstraintChange = true;
			richTextBox1.Clear();

			//RichEditBoxの「フォント」設定
			var obj = MarkDownSharpEditor.AppSettings.Instance;
			FontConverter fc = new FontConverter();
			try { richTextBox1.Font = (Font)fc.ConvertFromString(obj.FontFormat); }
			catch { }
			//RichEditBoxの「フォントカラー」設定
			richTextBox1.ForeColor = Color.FromArgb(obj.richEditForeColor);
			//ステータスバーに表示
			toolStripStatusLabelFontInfo.Text =	richTextBox1.Font.Name + "," + richTextBox1.Font.Size.ToString() + "pt";
	
			//テキストファイルの読み込み
			richTextBox1.Text = File.ReadAllText(FilePath, EditingFileEncoding);
			richTextBox1.Modified = false;

			//Undoバッファに追加
			undoCount = 0;
			UndoBuffer.Clear();
			UndoBuffer.Add(richTextBox1.Text.ToString());

			fConstraintChange = false;
			FormTextChange();

			//シンタックスハイライター
			SyntaxHightlighterWidthRegex();

			//PreviewToBrowser();
			if (timer1.Enabled == false)
			{
				timer1.Enabled = true;
			}

			return (true);

		}

		//----------------------------------------------------------------------
		// 新規ファイル（無題ファイル）を生成してそのパスを返す
		//----------------------------------------------------------------------
		private string CreateNoTitleFilePath()
		{
			int num = 1;
			string FilePath, FileName;
			string FileBaseName = "無題";
			string NumString = "";
			do 
			{
				FileName = FileBaseName + NumString + ".md";
				FilePath = Path.Combine(MarkDownSharpEditor.AppSettings.GetAppDataLocalPath(), FileName);
				NumString = String.Format("[{0:D4}]", num);
				num++;
			} while(File.Exists(FilePath));


			//ファイルが存在しているときは、上書きする
			File.WriteAllText(FilePath, "", Encoding.UTF8);

			return (FilePath);

		}
	
		//----------------------------------------------------------------------
		// 表示するHTMLのテンポラリファイルパスを取得する
		//----------------------------------------------------------------------
		private string GetTemporaryHtmlFilePath(string FilePath)
		{
			string DirPath = Path.GetDirectoryName(FilePath);
			string FileName = Path.GetFileNameWithoutExtension(FilePath);

			MD5 md5Hash = MD5.Create();
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(FileName));
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return(Path.Combine(DirPath, FileName + "_" + sBuilder.ToString() + ".html"));
        }

		//----------------------------------------------------------------------
		// 表示しているHTMLのテンポラリファイルを削除する
		//----------------------------------------------------------------------
		private void DeleteTemporaryHtmlFilePath()
		{
			string TempHtmlFilePath;

			if (MarkDownTextFilePath == "")
			{
				return;
			}

			TempHtmlFilePath = GetTemporaryHtmlFilePath(MarkDownTextFilePath);
			//見つかったときだけ削除
			if (File.Exists(TempHtmlFilePath) == true)
			{
				try
				{
					File.Delete(TempHtmlFilePath);
				}
				catch
				{
					MessageBox.Show(
						"テンポラリファイルの削除に失敗しました。編集中の場所に残った可能性があります。\n"+
						"このファイルは手動で削除していただいても問題ありません。\n" + TempHtmlFilePath,
						"エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		//----------------------------------------------------------------------
		// ブラウザープレビューの間隔を調整
		//----------------------------------------------------------------------
		private void timer1_Tick(object sender, EventArgs e)
		{
			if (MarkDownSharpEditor.AppSettings.Instance.AutoBrowserPreviewInterval < 0)
			{
				timer1.Enabled = false;
				return;
			}
			PreviewToBrowser();
			timer1.Enabled = false;
		}

		//----------------------------------------------------------------------
		// カーソル移動反映の間隔を調整
		//----------------------------------------------------------------------
		private void timer2_Tick(object sender, EventArgs e)
		{
			WebBrowserMoveCursor();
		}

		//----------------------------------------------------------------------
		// TODO: ブラウザプレビュー（編集中の内容を反映）
		//----------------------------------------------------------------------
		private void PreviewToBrowser()
		{
			//更新抑制中のときはプレビューしない
			if (fConstraintChange == true)
			{
				return;
			}

			string BackgroundColorString;
			string EncodingName;

			//編集中のファイル名
			string FileName = Path.GetFileName(MarkDownTextFilePath);
			//指定のDOCTYPE
			HtmlHeader htmlHeader = new HtmlHeader();
			string DocType = htmlHeader.GetHtmlHeader(MarkDownSharpEditor.AppSettings.Instance.HtmlDocType);

			//マーキングの色づけ
			if (MarkDownSharpEditor.AppSettings.Instance.fHtmlHighLightColor == true)
			{
				Color ColorBackground = Color.FromArgb(MarkDownSharpEditor.AppSettings.Instance.HtmlHighLightColor);
				BackgroundColorString = ColorTranslator.ToHtml(ColorBackground);
			}
			else
			{
				BackgroundColorString = "none";
			}

			//指定のエンコーディング
			int CodePageNum = MarkDownSharpEditor.AppSettings.Instance.CodePageNumber;
			try
			{
				Encoding enc = Encoding.GetEncoding(CodePageNum);
				//ブラウザ表示に対応したエンコーディングか
				if (enc.IsBrowserDisplay == true)
				{
					EncodingName = enc.WebName;
				}
				else
				{
					EncodingName = "utf-8";
				}
			}
			catch
			{
				//エンコーディングの取得に失敗した場合
				EncodingName = "utf-8";
			}
			//ヘッダ
			string header = string.Format(
@"{0}
<html>
<head>
<meta http-equiv='Content-Type' content='text/html; charset={1}' />
<link rel='stylesheet' href='{2}' type='text/css' />
<style type='text/css'>
	 ._mk {{background-color:{3}}}
</style>
<title>{4}</title>
</head>
<body>
", 
			DocType,               //DOCTYPE
			EncodingName,          //エンコーディング
			SelectedCssFilePath,   //適用中のCSSファイル
			BackgroundColorString, //編集箇所の背景色
			FileName);             //タイトル（＝ファイル名）

			//フッタ
			string footer = "</body>\n</html>";

			//Markdownパース
			Markdown mkdwn = new Markdown();
			string ResultText = mkdwn.Transform(richTextBox1.Text);
			//表示するHTMLデータを作成
			ResultText = header + ResultText + footer;
			//エンコーディングしつつbyte値に変換する（richEditBoxは基本的にutf-8 = 65001）
			byte[] bytesData = Encoding.GetEncoding(CodePageNum).GetBytes(ResultText);


			if (fNoTitle == true)
			{
				//「無題」ファイルを編集中はナビゲートせずにそのまま書き込む
				ResultText = Encoding.GetEncoding(CodePageNum).GetString(bytesData);
				webBrowser1.Document.Write(ResultText);

				//ツールバーの「関連付けられたブラウザーを起動」を無効に
				toolStripButtonBrowserPreview.Enabled = false;

			}
			else
			{
				//テンポラリファイルパスを取得する
				if (TemporaryHtmlFilePath == "")
				{
					TemporaryHtmlFilePath = GetTemporaryHtmlFilePath(MarkDownTextFilePath);
				}
				//他のプロセスからのテンポラリファイルの参照と削除を許可して開く（でないと飛ぶ）
				using (FileStream fs = new FileStream(
					TemporaryHtmlFilePath,
					FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete))
				{
					fs.Write(bytesData, 0, bytesData.Length);
				}

				//ナビゲート
				webBrowser1.Navigate(@"file://" + TemporaryHtmlFilePath);
				richTextBox1.Focus();
				
				toolStripButtonBrowserPreview.Enabled = true;

			}

		}

		//----------------------------------------------------------------------
		// プレビューページが読み込まれたら編集中の箇所へ自動スクロールする
		//----------------------------------------------------------------------
		private void webBrowser1_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
		{
			//更新抑制中のときは抜ける
			if (fConstraintChange == true)
			{
				return;
			}

			//読み込んだページのエンコーディングを表示
			foreach (EncodingInfo ei in Encoding.GetEncodings())
			{
				if (ei.GetEncoding().IsBrowserDisplay == true)
				{
					if (ei.GetEncoding().WebName == webBrowser1.Document.Encoding)
					{
						toolStripStatusLabelHtmlEncoding.Text = ei.DisplayName;
						break;
					}
				}
			}

			//すでに移動済みの位置へ（以下のコメントアウト部分の処理を毎回呼ぶと重い）
			webBrowser1.Document.Window.ScrollTo(new Point(0, BrowserCurrentPos));

			/*
			HtmlElementCollection elements = webBrowser1.Document.Body.Children;
			foreach (HtmlElement element in elements)
			{
				if (element.GetAttribute("className") == "_mk_edit")
				{
					int ElementTop = element.OffsetRectangle.Top;
					HtmlElement TempElement = element.OffsetParent;
					while (TempElement != null)
					{
						ElementTop = ElementTop + TempElement.OffsetRectangle.Top;
						TempElement = TempElement.OffsetParent;
					}

					Point pt = richTextBox1.GetPositionFromCharIndex(richTextBox1.SelectionStart);
					int pos = ElementTop - pt.Y;
					if (pos < 0)
					{
						pos = 0;
					}
					webBrowser1.Document.Window.ScrollTo(new Point(0, pos));
					break;
				}
			}
			*/

		}

		//----------------------------------------------------------------------
		// HACK: RichTextBoxで指定の文字インデックスまでのパラグラフ数（行数）を取得する
		// 
		//----------------------------------------------------------------------
		private int GetParagraphCount(int CharIndex)
		{

			int i, c;
			int CharNum = 0;
			int LinesNum = 0;

			bool fCommentOut = false;
			bool fList = false;

			MatchCollection col;
			Regex[] rg = new Regex[8];

			//見出し１
			rg[0] = new Regex(@"^=+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//見出し２
			rg[1] = new Regex(@"^-+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//リスト（ "*", "+", "*", "0-9."）
			rg[2] = new Regex(@"^ {0,3}\*[ \t]+.*$|^ {0,3}\+[ \t]+.*$|^ {0,3}-[ \t]+.*$|^ {0,3}[0-9]+\.[ \t]+.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//強制ブレーク
			rg[3] = new Regex(@"  $", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//コメントアウト（はじまり・行頭）
			rg[4] = new Regex(@"^<!--", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//コメントアウト（はじまり・文中）
			rg[5] = new Regex(@"<!--", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//コメントアウト（おわり・行末）
			rg[6] = new Regex(@"-->$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			//コメントアウト（おわり・文中）
			rg[7] = new Regex(@"-->$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

			//カーソルのある物理行番号（＝段落数）を取得する
			for ( i = 0; i < richTextBox1.Lines.Count(); i++)
			{
				//-----------------------------------
				//空行は行数としてカウントしない
				if (richTextBox1.Lines[i] == "")
				{
					fList = false;

				}
				//-----------------------------------
				//空行は行数としてカウントしない
				else
				{
					for (c = 0; c < 8; c++)
					{
						col = rg[c].Matches(richTextBox1.Lines[i], 0);
						if (col.Count > 0)
						{
							break;
						}
					}

					switch (c)
					{
						case 0:	//見出し１
						case 1:	//見出し２
							if (i == 0)
							{
								LinesNum++;
							}
							else
							{
								if (richTextBox1.Lines[i - 1] == "")
								{
									LinesNum++;
								}
							}
							break;

						case 2:	//リスト
							fList = true;
							LinesNum++;
							break;

						case 3:	//強制ブレーク
							LinesNum++;
							break;

						case 4:	//コメントアウト（はじまり・行頭）
							fCommentOut = true;
							//その行末にコメントアウトの終わりがあった
							col = rg[6].Matches(richTextBox1.Lines[i], 0);
							if (col.Count > 0)
							{
								fCommentOut = false;
								break;
							}
							
							//行途中にコメントアウトがおわった
							col = rg[7].Matches(richTextBox1.Lines[i], 0);
							if (col.Count > 0)
							{
								LinesNum++;
								fCommentOut = false;
								break;
							}
							break;

						case 5:	//コメントアウト（はじまり・文中）
							LinesNum++;
							fCommentOut = true;
							//行途中にコメントアウトがおわった
							col = rg[7].Matches(richTextBox1.Lines[i], 0);
							if (col.Count > 0)
							{
								fCommentOut = false;
							}
							break;

						case 6:	//コメントアウト（おわり・行末）
							fCommentOut = false;
							break;

						case 7:	//コメントアウト（おわり・文中）
							LinesNum++;
							fCommentOut = false;
							break;

						default:

							if (fCommentOut == true)
							{
								//コメントアウト中はノーカウント
							}
							else if (fList == true)
							{
								//リスト中もノーカウント
							}
							else
							{
								LinesNum++;
							}
							break;

					}//end switch;

				}//end if (richTextBox1.Lines[i] == "");

				CharNum += (richTextBox1.Lines[i].Length + 1);	// +改行

				if (CharNum > CharIndex)
				{
					break;	//カーソルのある位置で抜ける
				}

			}// end for;

			//デバッグ
			//this.Text = LinesNum.ToString();
			
			return (LinesNum);

		}

		//----------------------------------------------------------------------
		// HACK: WebBrowserMoveCursor() カーソル位置を探索してブラウザーページを自動スクロール
		//----------------------------------------------------------------------
		private void WebBrowserMoveCursor()
		{

			int i;
			int ElementTop = 0;

			if (webBrowser1.Document == null)
			{
				return;
			}

			//RichTextBoxのカーソル位置にある文字インデックスを取得して
			//そこまでのパラグラフ数（物理行数）を取得する
			int CurrentCharNum = richTextBox1.SelectionStart;
			int LinesNum = GetParagraphCount(CurrentCharNum);

			//前のマーキングを外す
			foreach (HtmlElement elm in webBrowser1.Document.Body.All)
			{
				if (elm.GetAttribute("className") == "_mk")
				{
					elm.SetAttribute("className", "");
					break;
				}
			}

			i = 1;
			foreach (HtmlElement elm in webBrowser1.Document.Body.Children)
			{
				if (elm.TagName == "!")           // コメントアウトは無視
				{
					continue;
				}
				else if (elm.TagName.ToUpper() == "SCRIPT") // スクリプトも無視
				{
					continue;
				}
				else if (elm.TagName.ToUpper() == "UL")     // 箇条書き（中身 <li> だけカウント）
				{
					foreach (HtmlElement elmLi in elm.Children)
					{
						if (i > LinesNum - 1)
						{
							elmLi.SetAttribute("className", "_mk");	//マーキングするためのクラス名を付加
							ElementTop = elmLi.OffsetRectangle.Top;
							break;
						}
						else
						{
							i++;
						}
					}
					i = i - 1;
				}
					
				if (i > LinesNum - 1)
				{
					if (ElementTop == 0)
					{
						elm.SetAttribute("className", "_mk");	//マーキングするためのクラス名を付加
						ElementTop = elm.OffsetRectangle.Top;
					}

					Point pt = richTextBox1.GetPositionFromCharIndex(richTextBox1.SelectionStart);
					int pos = ElementTop - pt.Y;
					if (pos < 0)
					{
						pos = 0;
					}
					webBrowser1.Document.Window.ScrollTo(new Point(0, pos));
					return;
				}

				i++;

			}

		}

		//----------------------------------------------------------------------
		// ブラウザーのナビゲート完了
		//----------------------------------------------------------------------
		private void webBrowser1_Navigated(object sender, WebBrowserNavigatedEventArgs e)
		{
			//ブラウザー操作ボタンの有効・無効化
			toolStripButtonBack.Enabled = webBrowser1.CanGoBack;
			toolStripButtonForward.Enabled = webBrowser1.CanGoForward;

		}

		//----------------------------------------------------------------------
		// TODO: HTML形式出力
		//----------------------------------------------------------------------
		private bool OutputToHtmlFile(string FilePath, string SaveToFilePath, bool fToClipboard = false)
		{

			if ( File.Exists(FilePath) == false )
			{
				return (false);
			}

			//出力内容
			string ResultText = "";
			//HTMLタグ
			string HeaderString = "";
			string  FooterString = "";
			//文字コード
			string EncodingName;
			Encoding encRead = Encoding.UTF8;
			Encoding encHtml = Encoding.UTF8;

			//-----------------------------------
			//編集中のファイルパス or 投げ込まれたファイルパス
			string FileName = Path.GetFileName(FilePath);

			//-----------------------------------
			//指定のDOCTYPE
			HtmlHeader htmlHeader = new HtmlHeader();
			string DocType = htmlHeader.GetHtmlHeader(MarkDownSharpEditor.AppSettings.Instance.HtmlDocType);
			//Web用の相対パス
			string CssPath = RerativeFilePath(FilePath, SelectedCssFilePath);

			//-----------------------------------
			//指定のエンコーディング
			int CodePageNum = MarkDownSharpEditor.AppSettings.Instance.CodePageNumber;
			try
			{
				encHtml = Encoding.GetEncoding(CodePageNum);
				//ブラウザ表示に対応したエンコーディングか
				if (encHtml.IsBrowserDisplay == true)
				{
					EncodingName = encHtml.WebName;
				}
				else
				{
					EncodingName = "utf-8";
					encHtml = Encoding.UTF8;
				}
			}
			catch
			{
				//エンコーディングの取得に失敗した場合はデフォルト
				EncodingName = "utf-8";
				encHtml = Encoding.UTF8;
			}

			//HTMLのヘッダを挿入する
			if (MarkDownSharpEditor.AppSettings.Instance.fHtmlOutputHeader == true)
			{
				//CSSファイルを埋め込む
				if (MarkDownSharpEditor.AppSettings.Instance.HtmlCssFileOption == 0)
				{
					string CssContents = "";

					if (File.Exists(SelectedCssFilePath) == true)
					{
						using (StreamReader sr = new StreamReader(SelectedCssFilePath, encHtml))
						{
							CssContents = sr.ReadToEnd();
						}
					}

					//ヘッダ
					HeaderString = string.Format(
@"{0}
<html>
<head>
<meta http-equiv='Content-Type' content='text/html; charset={1}' />
<title>{2}</title>
<style>
<！--
{3}
-->
</style>
</head>
<body>
",
						DocType,         //DOCTYPE
						EncodingName,    //エンコーディング
						FileName,        //タイトル（＝ファイル名）
						CssContents);	 //CSSの内容
					}
					//metaタグ（外部リンキング）
					else
					{
						//ヘッダ
						HeaderString = string.Format(
@"{0}
<html>
<head>
<meta http-equiv='Content-Type' content='text/html; charset={1}' />
<link rel='stylesheet' href='{2}' type='text/css' />
<title>{3}</title>
</head>
<body>
",
					DocType,         //DOCTYPE
					EncodingName,    //エンコーディング
					CssPath,         //CSSファイル（相対パス）
					FileName);		 //タイトル（＝ファイル名）
				}
				//フッタ
				FooterString = "</body>\n</html>";
			}
			else
			{
				HeaderString = "";
				FooterString = "";
			}

			//Markdownパース
			Markdown mkdwn = new Markdown();
			
			//編集中のファイル（richEditBoxの内容）
			if (MarkDownTextFilePath == FilePath)
			{
				ResultText = mkdwn.Transform(richTextBox1.Text);
				//エンコーディング変換（richEditBoxは基本的にutf-8）
				ResultText = ConvertStringToEncoding(ResultText, Encoding.UTF8.CodePage, CodePageNum);
			}
			else
			{
				//テキストファイルを開いてその文字コードに従って読み込み
				byte[] bs;
				using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
				{
					bs = new byte[fs.Length];
					fs.Read(bs, 0, bs.Length);
				}
				//文字コードを取得する
				encRead = GetCode(bs);
				//取得したバイト列を文字列に変換
				ResultText = encRead.GetString(bs);

				//UTF-8でないならいったん変換してパース
				if (encRead != Encoding.UTF8)
				{
					ResultText = ConvertStringToEncoding(ResultText, encRead.CodePage, CodePageNum);
				}
				ResultText = mkdwn.Transform(ResultText);
			}
	
			//ヘッダ＋本文＋フッタ
			ResultText = HeaderString + ResultText + FooterString;
			//出力するHTMLファイルの文字コードに合わせる
			ResultText = ConvertStringToEncoding(ResultText, Encoding.UTF8.CodePage, CodePageNum);

			if (fToClipboard == true)
			{	//クリップボードに書き込む
				Clipboard.SetText(ResultText);
			}
			else
			{	//ファイルに書き込む
				using (StreamWriter sw = new StreamWriter(SaveToFilePath, false, encHtml))
				{
					sw.Write(ResultText);
				}
			}
			return (true);

		}

		//----------------------------------------------------------------------
		// TODO: HTML形式ファイルへのバッチ出力
		//----------------------------------------------------------------------
		private bool BatchOutputToHtmlFiles(string[] ArrrayFileList)
		{
			string OutputFilePath;
			foreach ( string FilePath in ArrrayFileList )
			{
				OutputFilePath = Path.ChangeExtension(FilePath, ".html");
				if (OutputToHtmlFile(FilePath, OutputFilePath, false) == false)
				{
					return (false);
				}
			}

			return (true);

		}

		//----------------------------------------------------------------------
		// 基本パスから相対パスを取得する
		//----------------------------------------------------------------------
		private string RerativeFilePath(string BaseFilePath, string TargetFilePath)
		{
			Uri u1 = new Uri(BaseFilePath);
			Uri u2 = new Uri(u1, TargetFilePath);
			string RelativeFilePath = u1.MakeRelativeUri(u2).ToString();
			//URLデコードして、"/" を "\" に変更する
			RelativeFilePath = System.Web.HttpUtility.UrlDecode(RelativeFilePath).Replace('/', '\\');
			return (RelativeFilePath);
		}

		//----------------------------------------------------------------------
		// テキストを指定のエンコーディング文字列に変換する
		//----------------------------------------------------------------------
		public string ConvertStringToEncoding(string source, int SrcCodePage, int DestCodePage)
		{
			Encoding srcEnc;
			Encoding destEnc;
			try
			{
				srcEnc = Encoding.GetEncoding(SrcCodePage);
				destEnc = Encoding.GetEncoding(DestCodePage);
			}
			catch
			{
				//指定のコードページがおかしい（取得できない）
				return (source);
			}
			//Byte配列で変換する
			byte[] srcByte = srcEnc.GetBytes(source);
			byte[] destByte = Encoding.Convert( srcEnc, destEnc, srcByte);
			char[] destChars = new char[destEnc.GetCharCount(destByte, 0, destByte.Length)];
			destEnc.GetChars(destByte, 0, destByte.Length, destChars, 0);
			return new string(destChars);
		}


		//----------------------------------------------------------------------
		// シンタックス・ハイライター（Regex）のキーワード更新
		//----------------------------------------------------------------------
		private void RefreshSyntaxHightlighterKeyword()
		{
			var obj = MarkDownSharpEditor.AppSettings.Instance;
			MarkdownSyntaxKeywordAarray.Clear();
			//強制ブレーク
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"  $", Color.FromArgb(obj.ForeColor_LineBreak), Color.FromArgb(obj.BackColor_LineBreak)));
			//見出し１
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^#.*$", Color.FromArgb(obj.ForeColor_Headlines[1]), Color.FromArgb(obj.BackColor_Headlines[1])));
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^.*\n=+$", Color.FromArgb(obj.ForeColor_Headlines[1]), Color.FromArgb(obj.BackColor_Headlines[1])));
			//見出し２
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^##.*$", Color.FromArgb(obj.ForeColor_Headlines[2]), Color.FromArgb(obj.BackColor_Headlines[2])));
			//見出し３
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^###.*$", Color.FromArgb(obj.ForeColor_Headlines[3]), Color.FromArgb(obj.BackColor_Headlines[3])));
			//見出し４
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^####.*$", Color.FromArgb(obj.ForeColor_Headlines[4]), Color.FromArgb(obj.BackColor_Headlines[4])));
			//見出し５
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^#####.*$", Color.FromArgb(obj.ForeColor_Headlines[5]), Color.FromArgb(obj.BackColor_Headlines[5])));
			//見出し６
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^#####.*$", Color.FromArgb(obj.ForeColor_Headlines[6]), Color.FromArgb(obj.BackColor_Headlines[6])));
			//引用
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^>.*$", Color.FromArgb(obj.ForeColor_Blockquotes), Color.FromArgb(obj.BackColor_Blockquotes)));
			//リスト
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^ {0,3}\*[ \t]+.*$|^ {0,3}\+[ \t]+.*$|^ {0,3}-[ \t]+.*$|^ {0,3}[0-9]+\.[ \t]+.*$", Color.FromArgb(obj.ForeColor_Lists), Color.FromArgb(obj.BackColor_Lists)));
			//コードブロック
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^ {4,}$|^\t{1,}$", Color.FromArgb(obj.ForeColor_CodeBlocks), Color.FromArgb(obj.BackColor_CodeBlocks)));
			//罫線
			string horisontal_regex = @"^(\* ){3,}$|^\*.$|^(- ){3,}|^-{3,}$|^(_ ){3,}$|^_{3,}$";
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(horisontal_regex, Color.FromArgb(obj.ForeColor_Horizontal), Color.FromArgb(obj.BackColor_Horizontal)));
			//「見出し２」だけは罫線よりも後に
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"^.+\n-+$", Color.FromArgb(obj.ForeColor_Headlines[2]), Color.FromArgb(obj.BackColor_Headlines[2])));
			//リンク
			// [an example](http://example.com/ "Title") 
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"\[.*\]\((https?|ftp)(:\/\/[-_.!~*\'()a-zA-Z0-9;\/?:\@&=+\$,%#]+)[\t{1,}| {1,}]"".*""\)", Color.FromArgb(obj.ForeColor_Links), Color.FromArgb(obj.BackColor_Links)));
			// [This link](http://example.net/)
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"\[.*\]\((https?|ftp)(:\/\/[-_.!~*\'()a-zA-Z0-9;\/?:\@&=+\$,%#]+)\)", Color.FromArgb(obj.ForeColor_Links), Color.FromArgb(obj.BackColor_Links)));
			// [an example][id] 
			// [an example] [id]
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"\[.*\]\((https?|ftp)(:\/\/[-_.!~*\'()a-zA-Z0-9;\/?:\@&=+\$,%#]+)\)", Color.FromArgb(obj.ForeColor_Links), Color.FromArgb(obj.BackColor_Links)));
			// [id]: http://example.com/  "Optional Title Here"
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"\[.*\]:[\t{1,}| {1,}](https?|ftp)(:\/\/[-_.!~*\'()a-zA-Z0-9;\/?:\@&=+\$,%#]+)[\t{1,}| {1,}]"".*""",	Color.FromArgb(obj.ForeColor_Links), Color.FromArgb(obj.BackColor_Links)));
			//強調（em, em, strong, strong）
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"\*.*\*|_.*_|\*\*.*\*\*|__.*__", Color.FromArgb(obj.ForeColor_Emphasis), Color.FromArgb(obj.BackColor_Emphasis)));
			//ソースコード
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"`.*`", Color.FromArgb(obj.ForeColor_Code), Color.FromArgb(obj.BackColor_Emphasis)));
			//画像
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"!\[.*\]\(.*\)|!\[.*\]\[.*\]|\[.*\]: .*"".*""", Color.FromArgb(obj.ForeColor_Images), Color.FromArgb(obj.BackColor_Emphasis)));
			//自動リンク（メールアドレスとURL）
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"<(https?|ftp)(:\/\/[-_.!~*\'()a-zA-Z0-9;\/?:\@&=+\$,%#]+)>", Color.FromArgb(obj.ForeColor_Links), Color.FromArgb(obj.BackColor_Links)));
			string mail_regex =	@"<(?:(?:(?:(?:[a-zA-Z0-9_!#\$\%&'*+/=?\^`{}~|\-]+)(?:\.(?:[a-zA-Z0-9_!#\$\%&'*+/=?\^`{}~|\-]+))*)|(?:""(?:\\[^\r\n]|[^\\""])*"")))\@(?:(?:(?:(?:[a-zA-Z0-9_!#\$\%&'*+/=?\^`{}~|\-]+)(?:\.(?:[a-zA-Z0-9_!#\$\%&'*+/=?\^`{}~|\-]+))*)|(?:\[(?:\\\S|[\x21-\x5a\x5e-\x7e])*\])))>";
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(mail_regex, Color.FromArgb(obj.ForeColor_Links), Color.FromArgb(obj.BackColor_Links)));
			//コメントアウト（複数行含めたコメント全部）
			MarkdownSyntaxKeywordAarray.Add(new MarkdownSyntaxKeyword(@"<!--((?:.|\n)+)-->", Color.FromArgb(obj.ForeColor_Comments), Color.FromArgb(obj.BackColor_Comments)));

		}

		//----------------------------------------------------------------------
		// TODO: シンタックス・ハイライター（Regex）
		//----------------------------------------------------------------------
		private void SyntaxHightlighterWidthRegex(int SearchStartIndex = 0)
		{

			// TODO: シンタックス・ハイライター（今は仮に開始値=0）
			//SearchStartIndex = 0;

			var obj = MarkDownSharpEditor.AppSettings.Instance;

			Font fc = richTextBox1.Font;			//現在のフォント設定
			bool fModify = richTextBox1.Modified;	//現在の編集状況
			
			fConstraintChange = true;

			//現在のカーソル位置
			int selectStart = this.richTextBox1.SelectionStart;
			int selectEnd = richTextBox1.SelectionLength;
			//現在のスクロール位置
			Win32.POINT pt = GetScrollPos();
			//描画停止
			richTextBox1.BeginUpdate();

			// RichTextBoxの書式をクリアする
			// TODO: これだとテキストボックス内すべての書式がクリアされるので、指定された位置からのクリア処理が必要。
			string TemporaryText = richTextBox1.Text.ToString();
			//richTextBox1.Text = richTextBox1.Text.ToString();
			richTextBox1.Clear();
			richTextBox1.Text = TemporaryText;
			richTextBox1.Font = fc;
			richTextBox1.ForeColor = Color.FromArgb(obj.ForeColor_MainText);
			richTextBox1.BackColor = Color.FromArgb(obj.BackColor_MainText);
				
			foreach ( MarkdownSyntaxKeyword mk in MarkdownSyntaxKeywordAarray )
			{
				Regex r = new Regex(mk.RegText, RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
				MatchCollection col = r.Matches(richTextBox1.Text, SearchStartIndex);

				if ( col.Count > 0 )
				{
					foreach (Match m in col)
					{
						richTextBox1.Select(m.Groups[0].Index, m.Groups[0].Length);
						richTextBox1.SelectionColor = mk.ForeColor;        // 前景色
						richTextBox1.SelectionBackColor = mk.BackColor;	   // 背景色
					}
				}
			}

			//カーソル位置を戻す
			richTextBox1.Select(selectStart, selectEnd);
			//スクロール位置を戻す
			SetScrollPos(pt);
			//描画再開
			richTextBox1.EndUpdate();
			richTextBox1.Modified = fModify;

			fConstraintChange = false;

		}
		//----------------------------------------------------------------------
		// シンタックス・ハイライター（IndexOf版）
		//----------------------------------------------------------------------
		/*
		private void SyntaxHightlighterWidthIndexOf(string word, Color forecolor,  Color backcolor, int startIndex)
		{
			if (richTextBox1.Text.Contains(word))
			{
				int index = -1;
				//現在のカーソル位置
				int selectStart = this.richTextBox1.SelectionStart;
				int selectEnd = richTextBox1.SelectionLength;
				//現在のスクロール位置
				Win32.POINT pt = GetScrollPos();
				//描画停止
				richTextBox1.BeginUpdate();

				while ((index = this.richTextBox1.Text.IndexOf(word, (index + 1))) != -1)
				{
					richTextBox1.Select((index + startIndex), word.Length);
					richTextBox1.SelectionColor = forecolor;        // 前景色
					richTextBox1.SelectionBackColor = backcolor;	// 背景色
					richTextBox1.Select(selectStart, 0);
					richTextBox1.SelectionColor = Color.Black;
				}

				//カーソル位置を戻す
				richTextBox1.Select(selectStart, selectEnd);
				//スクロール位置を戻す
				SetScrollPos(pt);
				//描画再開
				richTextBox1.EndUpdate();
			}
		}
		*/

		//----------------------------------------------------------------------
		#region Scrollbar positions functions
		//----------------------------------------------------------------------
		/// <summary>
		/// Sends a win32 message to get the scrollbars' position.
		/// </summary>
		/// <returns>a POINT structore containing horizontal and vertical scrollbar position.</returns>
		private unsafe Win32.POINT GetScrollPos()
		{
			Win32.POINT res = new Win32.POINT();
			IntPtr ptr = new IntPtr(&res);
			Win32.SendMessage(richTextBox1.Handle, Win32.EM_GETSCROLLPOS, 0, ptr);
			return res;

		}

		/// <summary>
		/// Sends a win32 message to set scrollbars position.
		/// </summary>
		/// <param name="point">a POINT conatining H/Vscrollbar scrollpos.</param>
		private unsafe void SetScrollPos(Win32.POINT point)
		{
			IntPtr ptr = new IntPtr(&point);
			Win32.SendMessage(richTextBox1.Handle, Win32.EM_SETSCROLLPOS, 0, ptr);

		}
		#endregion
		//----------------------------------------------------------------------


		//======================================================================
		#region ブラウザーのツールバーメニュー
		//======================================================================

		//----------------------------------------------------------------------
		// ブラウザの「戻る」
		//----------------------------------------------------------------------
		private void toolStripButtonBack_Click(object sender, EventArgs e)
		{
			if (webBrowser1.CanGoBack == true)
			{
				webBrowser1.GoBack();
			}
		}

		//----------------------------------------------------------------------
		// ブラウザの「進む」
		//----------------------------------------------------------------------
		private void toolStripButtonForward_Click(object sender, EventArgs e)
		{
			if (webBrowser1.CanGoForward == true)
			{
				webBrowser1.GoForward();
			}

		}

		//----------------------------------------------------------------------
		// ブラウザの「更新」
		//----------------------------------------------------------------------
		private void toolStripButtonRefresh_Click(object sender, EventArgs e)
		{
			//手動更新設定
			if ( MarkDownSharpEditor.AppSettings.Instance.fAutoBrowserPreview == false ){
				//プレビューしているのは編集中のファイルか
				if (webBrowser1.Url.AbsoluteUri == @"file://" + TemporaryHtmlFilePath)
				{
					PreviewToBrowser();
				}
			}

			webBrowser1.Refresh();
		}

		//----------------------------------------------------------------------
		// ブラウザの「中止」
		//----------------------------------------------------------------------
		private void toolStripButtonStop_Click(object sender, EventArgs e)
		{
			webBrowser1.Stop();
		}

		//----------------------------------------------------------------------
		// 規定のブラウザーを関連付け起動してプレビュー
		//----------------------------------------------------------------------
		private void toolStripButtonBrowserPreview_Click(object sender, EventArgs e)
		{
			if (File.Exists(TemporaryHtmlFilePath) == true)
			{
				System.Diagnostics.Process.Start(TemporaryHtmlFilePath);
			}
			else
			{
				TemporaryHtmlFilePath = "";
			}
		}


		#endregion
		//======================================================================

		//======================================================================
		#region メインメニューイベント
		//======================================================================

		//-----------------------------------
		//「新しいファイルを開く」メニュー
		//-----------------------------------
		private void menuNewFile_Click(object sender, EventArgs e)
		{

			if (richTextBox1.Modified == true )
			{
				DialogResult ret = MessageBox.Show("編集中のファイルがあります。保存してから新しいファイルを開きますか？",
				"問い合わせ",  MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

				if (ret == DialogResult.Yes)
				{
					if (SaveToEditingFile() == true)
					{
						fNoTitle = false;	//無題フラグOFF
					}
					else
					{
						//キャンセルで抜けてきた
						return;
					}
				}
				else if (ret == DialogResult.Cancel)
				{
					return;
				}
			}

			//前の編集していたテンポラリを削除する
			DeleteTemporaryHtmlFilePath();

			//無題ファイルのまま編集しているのなら削除
			if (fNoTitle == true)
			{
				if (File.Exists(MarkDownTextFilePath) == true)
				{
					try
					{
						File.Delete(MarkDownTextFilePath);
					}
					catch
					{
					}
				}
			}

			//編集履歴に残す
			if (File.Exists(MarkDownTextFilePath) == true)
			{
				foreach (AppHistory data in MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles)
				{
					if (data.md == MarkDownTextFilePath)
					{ //いったん削除して
						MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Remove(data);
						break;
					}
				}
				AppHistory HistroyData = new AppHistory();
				HistroyData.md = MarkDownTextFilePath;
				HistroyData.css = SelectedCssFilePath;
				MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Insert(0, HistroyData);	//先頭に挿入
			}

			fConstraintChange = true;
			
			//ブラウザを空白にする
			webBrowser1.Navigate("about:blank");
			//テンポラリファイルがあれば削除
			DeleteTemporaryHtmlFilePath();
			//編集中のファイル情報をクリア
			MarkDownTextFilePath = CreateNoTitleFilePath();

			fNoTitle = true;
			richTextBox1.Text = "";
			richTextBox1.Modified = false;
			FormTextChange();
			fConstraintChange = false;

		}

		//-----------------------------------
		//「新しいウィンドウを開く」メニュー
		//-----------------------------------
		private void menuNewWindow_Click(object sender, EventArgs e)
		{
			//自分自身を起動する
			System.Diagnostics.ProcessStartInfo pInfo = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath);
			pInfo.Arguments = "/new";
			System.Diagnostics.Process p = System.Diagnostics.Process.Start(pInfo);

		}

		//-----------------------------------
		//「ファイルを開く」メニュー
		//-----------------------------------
		private void menuOpenFile_Click(object sender, EventArgs e)
		{
			OpenFile("");
		}

		//-----------------------------------
		//「ファイル」メニュー
		//-----------------------------------
		private void menuFile_Click(object sender, EventArgs e)
		{
			//編集履歴のサブメニューをつくる
			for (int i = 0; i < MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles.Count; i++)
			{
				AppHistory History = (AppHistory)MarkDownSharpEditor.AppSettings.Instance.ArrayHistoryEditedFiles[i];
				ToolStripMenuItem m = new ToolStripMenuItem(History.md);
				m.Tag = History.css;
				m.Click += new EventHandler(HistorySubMenuItemClickHandler);
				menuHistoryFiles.DropDownItems.Add(m);
			}

		}
		//-----------------------------------
		//各履歴メニューがクリックされたとき
		private void HistorySubMenuItemClickHandler(object sender, EventArgs e)
		{

			ToolStripMenuItem clickItem = (ToolStripMenuItem)sender;

			string FilePath = clickItem.Text;

			if (File.Exists(FilePath) == true)
			{
				OpenFile(FilePath);
			}
		}

		//-----------------------------------
		//編集中のファイルを保存する
		//-----------------------------------
		private bool SaveToEditingFile(bool fSaveAs = false)
		{
			//名前が付けられていない、または別名保存指定なのでダイアログ表示
			if (fNoTitle == true || fSaveAs == true)
			{
				if (saveFileDialog1.ShowDialog() == DialogResult.OK)
				{
					using (StreamWriter sw = new StreamWriter(
						saveFileDialog1.FileName,
						false,
						EditingFileEncoding))
					{
						sw.Write(richTextBox1.Text);
					}
				}
				else
				{
					return (false);
				}
			}
			else
			{
				//上書き保存
				using (StreamWriter sw = new StreamWriter(
					MarkDownTextFilePath,
					false,
					EditingFileEncoding))
				{
					sw.Write(richTextBox1.Text);
				}

			}

			//Undoバッファクリア
			undoCount = 0;
			UndoBuffer.Clear();

			fNoTitle = false;	//無題フラグOFF
			richTextBox1.Modified = false;
			FormTextChange();

			return(true);
		}

		//-----------------------------------
		//「ファイルを保存」メニュー
		//-----------------------------------
		private void menuSaveFile_Click(object sender, EventArgs e)
		{
		  SaveToEditingFile();
		}

		//-----------------------------------
		//「名前を付けてファイルを保存」メニュー
		//-----------------------------------
		private void menuSaveAsFile_Click(object sender, EventArgs e)
		{
		  SaveToEditingFile(true);
		}

		//-----------------------------------
		//「HTMLファイル出力(&P)」メニュー
		//-----------------------------------
		private void menuOutputHtmlFile_Click(object sender, EventArgs e)
		{
			string OutputFilePath;
			string DirPath = Path.GetDirectoryName(MarkDownTextFilePath);

			if ( File.Exists(MarkDownTextFilePath) == true ){
				saveFileDialog2.InitialDirectory = DirPath;
			}

			//保存ダイアログを表示する
			if (MarkDownSharpEditor.AppSettings.Instance.fShowHtmlSaveDialog == true)
			{
				if (saveFileDialog2.ShowDialog() == DialogResult.OK)
				{
					OutputToHtmlFile(MarkDownTextFilePath, saveFileDialog2.FileName, false);
				}
			}
			else
			{
				//ダイアログを抑制しているので編集中のファイルのディレクトリへ保存する
				OutputFilePath = Path.Combine(DirPath, Path.GetFileNameWithoutExtension(MarkDownTextFilePath)) + ".html";

				if (File.Exists(OutputFilePath) == true)
				{
					DialogResult ret = MessageBox.Show(
						"すでに同名のファイルが存在しています。上書きして出力しますか？\n" + OutputFilePath,
						"問い合わせ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
						MessageBoxDefaultButton.Button1);	// Yesがデフォルト

					if (ret == DialogResult.Yes)
					{
						//上書きしてHTMLファイルへ出力
						OutputToHtmlFile(MarkDownTextFilePath, OutputFilePath, false);
					}
					else if (ret == DialogResult.No)
					{
						//設定されてないが一応保存ダイアログを出す
						if (saveFileDialog2.ShowDialog() == DialogResult.OK)
						{
							//HTMLファイルへ出力
							OutputToHtmlFile(MarkDownTextFilePath, saveFileDialog2.FileName, false);
						}

					}
					else
					{
						//キャンセル
					}
				}
				else
				{
					//HTMLファイルへ出力
					OutputToHtmlFile(MarkDownTextFilePath, OutputFilePath, false);
				}
			}
		}

		//-----------------------------------
		//「HTMLソースコードをクリップボードへコピー(&L)」メニュー
		//-----------------------------------
		private void menuOutputHtmlToClipboard_Click(object sender, EventArgs e)
		{
			//HTMLソースをクリップボードへ出力
			OutputToHtmlFile(MarkDownTextFilePath, "", true);
	
			//HTMLソースをクリップボードコピーしたときに確認メッセージを表示する(&M)
			if (MarkDownSharpEditor.AppSettings.Instance.fShowHtmlToClipboardMessage == true)
			{
				MessageBox.Show(
					"クリップボードに保存されました。",
					"通知", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}

		}

		//-----------------------------------
		//「終了」メニュー
		//-----------------------------------
		private void menuExit_Click(object sender, EventArgs e)
		{
		  Close();
		}

		//-----------------------------------
		//「編集」メニュー
		//-----------------------------------
		private void menuEdit_Click(object sender, EventArgs e)
		{
			if (undoCount > 0)
			{
				menuUndo.Enabled = true;
			}
			else
			{
				menuUndo.Enabled = false;
			}

			if (undoCount == UndoBuffer.Count)
			{
				menuRedo.Enabled = false;
			}
			else
			{
				menuRedo.Enabled = true;
			}

			if (richTextBox1.SelectionLength > 0)
			{
				menuCut.Enabled = true;
				menuCopy.Enabled = true;
			}
			else
			{
				menuCut.Enabled = false;
				menuCopy.Enabled = false;
			}
		}
		//-----------------------------------
		//「元に戻す」メニュー
		//-----------------------------------
		private void menuUndo_Click(object sender, EventArgs e)
		{
			if (UndoBuffer.Count > 0 && undoCount > 0)
			{
				//現在のカーソル位置
				int selectStart = this.richTextBox1.SelectionStart;
				int selectEnd = richTextBox1.SelectionLength;
				//現在のスクロール位置
				Win32.POINT pt = GetScrollPos();
				//描画停止
				richTextBox1.BeginUpdate();

				undoCount--;
				richTextBox1.Text = UndoBuffer[undoCount];

				//カーソル位置を戻す
				richTextBox1.Select(selectStart, selectEnd);
				//スクロール位置を戻す
				SetScrollPos(pt);
				//描画再開
				richTextBox1.EndUpdate();

				if (undoCount == 0)
				{
					richTextBox1.Modified = false;
					FormTextChange();
				}

			}

		}
		//-----------------------------------
		//「やり直す」メニュー
		//-----------------------------------
		private void menuRedo_Click(object sender, EventArgs e)
		{
			if (undoCount < UndoBuffer.Count)
			{
				undoCount++;
				richTextBox1.Text = UndoBuffer[undoCount];
				FormTextChange();
			}
		}
		//-----------------------------------
		//「切り取り」メニュー
		//-----------------------------------
		private void menuCut_Click(object sender, EventArgs e)
		{
		  if (richTextBox1.SelectionLength > 0)
		  {
			richTextBox1.Cut();
			FormTextChange();
		  }
		}
		//-----------------------------------
		//「コピー」メニュー
		//-----------------------------------
		private void menuCopy_Click(object sender, EventArgs e)
		{
		  if (richTextBox1.SelectionLength > 0)
		  {
			richTextBox1.Copy();
		  }
		}
		//-----------------------------------
		//「貼り付け」メニュー
		//-----------------------------------
		private void menuPaste_Click(object sender, EventArgs e)
		{
		  IDataObject data = Clipboard.GetDataObject();
		  if (data != null && data.GetDataPresent(DataFormats.Text) == true)
		  {
			DataFormats.Format fmt = DataFormats.GetFormat(DataFormats.Text);
			richTextBox1.Paste(fmt);
			FormTextChange();
		  }

		}
		//-----------------------------------
		//「すべてを選択」メニュー
		//-----------------------------------
		private void menuSelectAll_Click(object sender, EventArgs e)
		{
		  richTextBox1.SelectAll();
		}

		//-----------------------------------
		// 表示の更新
		//-----------------------------------
		private void menuViewRefresh_Click(object sender, EventArgs e)
		{
			PreviewToBrowser();
		}

		//-----------------------------------
		//「ソースとビューを均等表示する」メニュー
		//-----------------------------------
		private void menuViewWidthEvenly_Click(object sender, EventArgs e)
		{
			if (menuViewWidthEvenly.Checked == true)
			{
				menuViewWidthEvenly.Checked = false;
			}
			else
			{
				menuViewWidthEvenly.Checked = true;
			}
			MarkDownSharpEditor.AppSettings.Instance.fSplitBarWidthEvenly = menuViewWidthEvenly.Checked;
		}

		//-----------------------------------
		//「ツールバーを表示する」メニュー
		//-----------------------------------
		private void menuViewToolBar_Click(object sender, EventArgs e)
		{
			if (menuViewToolBar.Checked == true)
			{
				menuViewToolBar.Checked = false;
				toolStrip1.Visible = false;
			}
			else
			{
				menuViewToolBar.Checked = true;
				toolStrip1.Visible = true;
			}
			MarkDownSharpEditor.AppSettings.Instance.fViewToolBar = toolStrip1.Visible;
		}
		//-----------------------------------
		//「ステータスバーを表示する」メニュー
		//-----------------------------------
		private void menuViewStatusBar_Click(object sender, EventArgs e)
		{
			if (menuViewStatusBar.Checked == true)
			{
				menuViewStatusBar.Checked = false;
				statusStrip1.Visible = false;
			}
			else
			{
				menuViewStatusBar.Checked = true;
				statusStrip1.Visible = true;
			}
			MarkDownSharpEditor.AppSettings.Instance.fViewStatusBar = statusStrip1.Visible;
		}

		//-----------------------------------
		//「書式フォント」メニュー
		//-----------------------------------
		private void menuFont_Click(object sender, EventArgs e)
		{

			bool fMod = richTextBox1.Modified;

			fontDialog1.Font = richTextBox1.Font;
			fontDialog1.Color = richTextBox1.ForeColor;

			//選択できるポイントサイズの最小・最大値
			fontDialog1.MinSize = 6;
			fontDialog1.MaxSize = 72;
			fontDialog1.FontMustExist = true;
			//横書きフォントだけを表示する
			fontDialog1.AllowVerticalFonts = false;
			//色を選択できるようにする
			fontDialog1.ShowColor = true;
			//取り消し線、下線、テキストの色などのオプションを指定不可
			fontDialog1.ShowEffects = false;

			//ダイアログを表示する
			if (fontDialog1.ShowDialog() == DialogResult.OK)
			{
				richTextBox1.Font = fontDialog1.Font;
				richTextBox1.ForeColor = fontDialog1.Color;
				//ステータスバーに表示
				toolStripStatusLabelFontInfo.Text =
					fontDialog1.Font.Name + "," + fontDialog1.Font.Size.ToString() + "pt";
			}

			//シンタックスハイライターを全体に反映
			SyntaxHightlighterWidthRegex();
			
			//richTextBoxの書式を変えても「変更」となるので元のステータスへ戻す
			richTextBox1.Modified = fMod;

		}

		//-----------------------------------
		//「オプション」メニュー
		//-----------------------------------
		private void menuOption_Click(object sender, EventArgs e)
		{
			Form3 frm3 = new Form3();
			frm3.ShowDialog();
			frm3.Dispose();

			RefreshSyntaxHightlighterKeyword();	//キーワードリストの更新
			SyntaxHightlighterWidthRegex(0);     //色分け

			//設定を再読込
			MarkDownSharpEditor.AppSettings.Instance.ReadFromXMLFile();
			//プレビュー間隔を更新
			if (MarkDownSharpEditor.AppSettings.Instance.AutoBrowserPreviewInterval > -1)
			{
				timer1.Interval = MarkDownSharpEditor.AppSettings.Instance.AutoBrowserPreviewInterval;
			}
		}

		//-----------------------------------
		// ヘルプファイルの表示
		//-----------------------------------
		private void menuContents_Click(object sender, EventArgs e)
		{
			string DirPath = MarkDownSharpEditor.AppSettings.GetAppDataLocalPath();
			string HelpFilePath = Path.Combine(DirPath, "help.md");

			if (File.Exists(HelpFilePath) == true)
			{   //別ウィンドウで開く
				//Create a new ProcessStartInfo structure.
				System.Diagnostics.ProcessStartInfo pInfo = new System.Diagnostics.ProcessStartInfo();
				//Set the file name member. 
				pInfo.FileName = HelpFilePath;
				//UseShellExecute is true by default. It is set here for illustration.
				pInfo.UseShellExecute = true;
				System.Diagnostics.Process p = System.Diagnostics.Process.Start(pInfo);
			}
			else
			{
				MessageBox.Show("ヘルプファイルがありません。開くことができませんでした。",
					"エラー", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}

		}

		//-----------------------------------
		// サンプル表示
		//-----------------------------------
		private void menuViewSample_Click(object sender, EventArgs e)
		{
			string DirPath = MarkDownSharpEditor.AppSettings.GetAppDataLocalPath();
			string SampleFilePath = Path.Combine(DirPath, "sample.md");

			if (File.Exists(SampleFilePath) == true)
			{
				System.Diagnostics.ProcessStartInfo pInfo = new System.Diagnostics.ProcessStartInfo();
				pInfo.FileName = SampleFilePath;
				pInfo.UseShellExecute = true;
				System.Diagnostics.Process p = System.Diagnostics.Process.Start(pInfo);
			}
			else
			{
				MessageBox.Show("サンプルファイルがありません。開くことができませんでした。",
					"エラー", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
		}

		//-----------------------------------
		//「MarkDownSharpについて」メニュー
		//-----------------------------------
		private void menuAbout_Click(object sender, EventArgs e)
		{
			Form2 frm2 = new Form2();
			frm2.ShowDialog();
			frm2.Dispose();
		}

		#endregion
		//======================================================================

		//======================================================================
		#region ステータスバーイベント
		//======================================================================

		//-----------------------------------
		// ステータスバー（CSS）
		//-----------------------------------
		private void toolStripStatusLabelCssFileName_Click(object sender, EventArgs e)
		{
			//ポップアップメニューに登録する
			contextMenu1.Items.Clear();

			foreach (string FilePath in MarkDownSharpEditor.AppSettings.Instance.ArrayCssFileList)
			{
				ToolStripMenuItem item = new ToolStripMenuItem(Path.GetFileName(FilePath));
				item.Tag = FilePath;
				if (SelectedCssFilePath == FilePath )
				{
					item.Checked = true;
				}
				contextMenu1.Items.Add(item);
			}
			contextMenu1.Tag = "css";
			contextMenu1.Show(Control.MousePosition);

		}

		//-----------------------------------
		// ステータスバー（エンコーディング）
		//-----------------------------------
		private void toolStripStatusLabelEncoding_Click(object sender, EventArgs e)
		{
			//ポップアップメニューに登録する
			contextMenu1.Items.Clear();
			foreach (EncodingInfo ei in Encoding.GetEncodings())
			{
				if (ei.GetEncoding().IsBrowserDisplay == true)
				{
					ToolStripMenuItem item = new ToolStripMenuItem(ei.DisplayName);
					item.Tag = ei.CodePage;
					if (ei.CodePage == MarkDownSharpEditor.AppSettings.Instance.CodePageNumber)
					{
						item.Checked = true;
					}
					contextMenu1.Items.Add(item);
				}
			}
			contextMenu1.Tag = "encoding";
			contextMenu1.Show(Control.MousePosition);
		}

		//-----------------------------------
		// ステータスバー（共通のクリックイベント）
		//-----------------------------------
		private void contextMenu1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			//-----------------------------------
			// 適用するCSSファイル変更
			//-----------------------------------
			if ((string)contextMenu1.Tag == "css")
			{
				SelectedCssFilePath = (string)e.ClickedItem.Tag;
				toolStripStatusLabelCssFileName.Text = Path.GetFileName(SelectedCssFilePath);
				//プレビューも更新する
				PreviewToBrowser();

			}
			//-----------------------------------
			// 出力HTMLに適用する文字コードの変更
			//-----------------------------------
			else if ((string)contextMenu1.Tag == "encoding")
			{
				MarkDownSharpEditor.AppSettings.Instance.CodePageNumber = (int)e.ClickedItem.Tag;
				//プレビューも更新する
				PreviewToBrowser();
			}

		}

		#endregion
		//======================================================================

		//======================================================================
		#region エンコーディングの判定
		//======================================================================
		//
		// ここのコードはまんま、以下のサイトのものを使わせていただきました。
		// http://dobon.net/vb/dotnet/string/detectcode.html
		//
		// <summary>
		// 文字コードを判別する
		// </summary>
		// <remarks>
		// Jcode.pmのgetcodeメソッドを移植したものです。
		// Jcode.pm(http://openlab.ring.gr.jp/Jcode/index-j.html)
		// Jcode.pmのCopyright: Copyright 1999-2005 Dan Kogai
		// </remarks>
		// <param name="bytes">文字コードを調べるデータ</param>
		// <returns>適当と思われるEncodingオブジェクト。
		// 判断できなかった時はnull。</returns>
		public static Encoding GetCode(byte[] bytes)
		{
		  const byte bEscape = 0x1B;
		  const byte bAt = 0x40;
		  const byte bDollar = 0x24;
		  const byte bAnd = 0x26;
		  const byte bOpen = 0x28;	//'('
		  const byte bB = 0x42;
		  const byte bD = 0x44;
		  const byte bJ = 0x4A;
		  const byte bI = 0x49;

		  int len = bytes.Length;
		  byte b1, b2, b3, b4;

		  //Encode::is_utf8 は無視

		  bool isBinary = false;
		  for (int i = 0; i < len; i++)
		  {
			b1 = bytes[i];
			if (b1 <= 0x06 || b1 == 0x7F || b1 == 0xFF)
			{
			  //'binary'
			  isBinary = true;
			  if (b1 == 0x00 && i < len - 1 && bytes[i + 1] <= 0x7F)
			  {
				//smells like raw unicode
				return Encoding.Unicode;
			  }
			}
		  }
		  if (isBinary)
		  {
			return null;
		  }

		  //not Japanese
		  bool notJapanese = true;
		  for (int i = 0; i < len; i++)
		  {
			b1 = bytes[i];
			if (b1 == bEscape || 0x80 <= b1)
			{
			  notJapanese = false;
			  break;
			}
		  }
		  if (notJapanese)
		  {
			return Encoding.ASCII;
		  }

		  for (int i = 0; i < len - 2; i++)
		  {
			b1 = bytes[i];
			b2 = bytes[i + 1];
			b3 = bytes[i + 2];

			if (b1 == bEscape)
			{
			  if (b2 == bDollar && b3 == bAt)
			  {
				//JIS_0208 1978
				//JIS
				return Encoding.GetEncoding(50220);
			  }
			  else if (b2 == bDollar && b3 == bB)
			  {
				//JIS_0208 1983
				//JIS
				return Encoding.GetEncoding(50220);
			  }
			  else if (b2 == bOpen && (b3 == bB || b3 == bJ))
			  {
				//JIS_ASC
				//JIS
				return Encoding.GetEncoding(50220);
			  }
			  else if (b2 == bOpen && b3 == bI)
			  {
				//JIS_KANA
				//JIS
				return Encoding.GetEncoding(50220);
			  }
			  if (i < len - 3)
			  {
				b4 = bytes[i + 3];
				if (b2 == bDollar && b3 == bOpen && b4 == bD)
				{
				  //JIS_0212
				  //JIS
				  return Encoding.GetEncoding(50220);
				}
				if (i < len - 5 &&
					b2 == bAnd && b3 == bAt && b4 == bEscape &&
					bytes[i + 4] == bDollar && bytes[i + 5] == bB)
				{
				  //JIS_0208 1990
				  //JIS
				  return Encoding.GetEncoding(50220);
				}
			  }
			}
		  }

		  //should be euc|sjis|utf8
		  //use of (?:) by Hiroki Ohzaki <ohzaki@iod.ricoh.co.jp>
		  int sjis = 0;
		  int euc = 0;
		  int utf8 = 0;
		  for (int i = 0; i < len - 1; i++)
		  {
			b1 = bytes[i];
			b2 = bytes[i + 1];
			if (((0x81 <= b1 && b1 <= 0x9F) || (0xE0 <= b1 && b1 <= 0xFC)) &&
				((0x40 <= b2 && b2 <= 0x7E) || (0x80 <= b2 && b2 <= 0xFC)))
			{
			  //SJIS_C
			  sjis += 2;
			  i++;
			}
		  }
		  for (int i = 0; i < len - 1; i++)
		  {
			b1 = bytes[i];
			b2 = bytes[i + 1];
			if (((0xA1 <= b1 && b1 <= 0xFE) && (0xA1 <= b2 && b2 <= 0xFE)) ||
				(b1 == 0x8E && (0xA1 <= b2 && b2 <= 0xDF)))
			{
			  //EUC_C
			  //EUC_KANA
			  euc += 2;
			  i++;
			}
			else if (i < len - 2)
			{
			  b3 = bytes[i + 2];
			  if (b1 == 0x8F && (0xA1 <= b2 && b2 <= 0xFE) &&
				  (0xA1 <= b3 && b3 <= 0xFE))
			  {
				//EUC_0212
				euc += 3;
				i += 2;
			  }
			}
		  }
		  for (int i = 0; i < len - 1; i++)
		  {
			b1 = bytes[i];
			b2 = bytes[i + 1];
			if ((0xC0 <= b1 && b1 <= 0xDF) && (0x80 <= b2 && b2 <= 0xBF))
			{
			  //UTF8
			  utf8 += 2;
			  i++;
			}
			else if (i < len - 2)
			{
			  b3 = bytes[i + 2];
			  if ((0xE0 <= b1 && b1 <= 0xEF) && (0x80 <= b2 && b2 <= 0xBF) &&
				  (0x80 <= b3 && b3 <= 0xBF))
			  {
				//UTF8
				utf8 += 3;
				i += 2;
			  }
			}
		  }
		  //M. Takahashi's suggestion
		  //utf8 += utf8 / 2;

		  System.Diagnostics.Debug.WriteLine(
			  string.Format("sjis = {0}, euc = {1}, utf8 = {2}", sjis, euc, utf8));
		  if (euc > sjis && euc > utf8)
		  {
			//EUC
			return Encoding.GetEncoding(51932);
		  }
		  else if (sjis > euc && sjis > utf8)
		  {
			//SJIS
			return Encoding.GetEncoding(932);
		  }
		  else if (utf8 > euc && utf8 > sjis)
		  {
			//UTF8
			return Encoding.UTF8;
		  }

		  return null;
		}

		#endregion
		//======================================================================
		







	}
}
