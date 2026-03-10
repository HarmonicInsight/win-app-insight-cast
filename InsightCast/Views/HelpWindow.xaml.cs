using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using InsightCast.Services;

namespace InsightCast.Views;

public partial class HelpWindow : Window
{
    private readonly string[] _sectionIds;
    private readonly string[] _sectionNames;
    private readonly bool _isEn;
    private bool _isLoaded;
    private string? _initialSection;
    private bool _suppressNavChange;
    private readonly DispatcherTimer _searchDebounce;

    public HelpWindow() : this(null) { }

    /// <summary>Context help: open to a specific section by ID (C7).</summary>
    public HelpWindow(string? sectionId)
    {
        InitializeComponent();

        _isEn = LocalizationService.CurrentLanguage == "EN";
        _initialSection = sectionId;

        Title = _isEn ? "Insight Training Studio - Help" : "Insight Training Studio - ヘルプ";
        TocHeader.Text = _isEn ? "Contents" : "目次";
        SearchPlaceholder.Text = _isEn ? "Search..." : "検索...";

        _sectionIds = new[]
        {
            "overview", "quickstart", "ui-layout", "quick-mode", "scene-editor", "narration",
            "subtitle-text", "bgm-effects", "motion-effects", "screen-tools",
            "planning", "pptx-create", "export", "batch-export",
            "shortcuts", "faq", "ai-assistant", "license", "system-req", "support"
        };

        _sectionNames = _isEn
            ? new[]
            {
                "Overview", "Quick Start", "UI Layout", "Quick Mode", "Scene Editor", "Narration",
                "Subtitles & Text", "BGM & Effects", "Motion Effects", "Screen Capture",
                "Planning Tab", "PPTX Creation", "Export", "Batch Export",
                "Keyboard Shortcuts", "FAQ", "AI Assistant", "License", "System Requirements", "Support"
            }
            : new[]
            {
                "はじめに", "クイックスタート", "画面構成", "クイックモード", "シーン編集", "ナレーション",
                "字幕・テキスト", "BGM・エフェクト", "モーションエフェクト", "画面キャプチャ",
                "企画・制作", "PPTX作成", "書き出し", "バッチエクスポート",
                "キーボードショートカット", "FAQ", "AIアシスタント", "ライセンス", "システム要件", "お問い合わせ"
            };

        NavList.ItemsSource = _sectionNames;
        NavList.SelectedIndex = 0;

        // Debounce timer for search (300ms delay to avoid DOM traversal on every keystroke)
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ExecuteSearch(SearchBox.Text.Trim());
        };

        HelpBrowser.LoadCompleted += OnBrowserLoadCompleted;
        HelpBrowser.NavigateToString(GenerateHtml(_isEn));

        Closed += OnWindowClosed;
    }

    private void OnBrowserLoadCompleted(object? sender, NavigationEventArgs e)
    {
        _isLoaded = true;
        // Jump to initial section if specified (C7)
        if (_initialSection != null)
        {
            NavigateToSection(_initialSection);
            // Sync nav list without triggering re-navigation
            var idx = Array.IndexOf(_sectionIds, _initialSection);
            if (idx >= 0)
            {
                _suppressNavChange = true;
                NavList.SelectedIndex = idx;
                _suppressNavChange = false;
            }
            _initialSection = null;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _searchDebounce.Stop();
        HelpBrowser.LoadCompleted -= OnBrowserLoadCompleted;
        HelpBrowser.Dispose();
    }

    private void NavigateToSection(string sectionId)
    {
        if (!_isLoaded || string.IsNullOrEmpty(sectionId)) return;
        try
        {
            var escaped = EscapeJsString(sectionId);
            HelpBrowser.InvokeScript("eval",
                $"var el=document.getElementById('{escaped}'); if(el) el.scrollIntoView({{behavior:'smooth',block:'start'}})");
        }
        catch { /* Ignore script errors */ }
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavChange || !_isLoaded) return;
        var idx = NavList.SelectedIndex;
        if (idx < 0 || idx >= _sectionIds.Length) return;
        NavigateToSection(_sectionIds[idx]);
    }

    // C5: Search functionality (debounced)
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

        if (!_isLoaded) return;

        // Restart debounce timer; clear immediately if empty
        _searchDebounce.Stop();
        if (string.IsNullOrEmpty(query))
            ExecuteSearch("");
        else
            _searchDebounce.Start();
    }

    private void ExecuteSearch(string query)
    {
        try
        {
            // Always clear previous highlights first
            HelpBrowser.InvokeScript("eval", """
                (function(){
                    var marks = document.querySelectorAll('mark.search-hl');
                    for(var i=0;i<marks.length;i++){
                        var parent = marks[i].parentNode;
                        parent.replaceChild(document.createTextNode(marks[i].textContent), marks[i]);
                        parent.normalize();
                    }
                })()
                """);

            if (string.IsNullOrEmpty(query) || query.Length < 2) return;

            var escaped = EscapeJsString(query);
            HelpBrowser.InvokeScript("eval", $@"
                (function(){{
                    var q = '{escaped}'.toLowerCase();
                    function walk(node){{
                        if(node.nodeType === 3){{
                            var idx = node.textContent.toLowerCase().indexOf(q);
                            if(idx >= 0){{
                                var span = document.createElement('mark');
                                span.className = 'search-hl';
                                span.style.background = '#FDE68A';
                                span.style.padding = '1px 2px';
                                span.style.borderRadius = '2px';
                                var after = node.splitText(idx);
                                after.splitText(q.length);
                                var hl = span.cloneNode(true);
                                hl.appendChild(document.createTextNode(after.textContent));
                                after.parentNode.replaceChild(hl, after);
                            }}
                        }} else if(node.nodeType === 1 && node.tagName !== 'SCRIPT' && node.tagName !== 'STYLE' && node.tagName !== 'MARK'){{
                            for(var i=0;i<node.childNodes.length;i++) walk(node.childNodes[i]);
                        }}
                    }}
                    walk(document.body);
                    var first = document.querySelector('mark.search-hl');
                    if(first) first.scrollIntoView({{behavior:'smooth',block:'center'}});
                }})()");
        }
        catch { /* Ignore script errors */ }
    }

    /// <summary>Escape a string for safe embedding in a JS single-quoted literal.</summary>
    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    private void HelpBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (e.Uri == null) return;

        if (e.Uri.Scheme.StartsWith("http", StringComparison.Ordinal))
        {
            e.Cancel = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.ToString(),
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore if browser launch fails
            }
        }
        else if (e.Uri.Scheme is "javascript" or "vbscript" or "data")
        {
            // Block potentially dangerous URI schemes
            e.Cancel = true;
        }
    }

    // ========== HTML Content Generation ==========

    private static string GenerateHtml(bool isEn)
    {
        var sb = new StringBuilder(64000);
        sb.Append(HtmlHead());

        if (isEn)
        {
            AppendEnglishContent(sb);
        }
        else
        {
            AppendJapaneseContent(sb);
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string GetVersion()
    {
        var version = typeof(HelpWindow).Assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    private static string HtmlHead()
    {
        return """
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <meta http-equiv="X-UA-Compatible" content="IE=edge">
            <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
                font-family: 'Yu Gothic UI', 'Meiryo', 'Segoe UI', sans-serif;
                background: #FFFFFF;
                color: #1C1917;
                line-height: 1.75;
                padding: 28px 36px 60px 36px;
                font-size: 13.5px;
            }
            h1 {
                color: #B8942F;
                font-size: 22px;
                font-weight: 700;
                border-bottom: 2px solid #B8942F;
                padding-bottom: 8px;
                margin: 36px 0 16px 0;
            }
            h1:first-child { margin-top: 0; }
            h2 {
                color: #B8942F;
                font-size: 16px;
                font-weight: 600;
                border-bottom: 1px solid #E7E2DA;
                padding-bottom: 6px;
                margin: 28px 0 12px 0;
            }
            h3 {
                color: #57534E;
                font-size: 14px;
                font-weight: 600;
                margin: 20px 0 8px 0;
            }
            p { margin: 8px 0; }
            ul, ol { margin: 8px 0 8px 24px; }
            li { margin: 4px 0; }
            table {
                border-collapse: collapse;
                width: 100%;
                margin: 12px 0;
                font-size: 13px;
            }
            th {
                background: #B8942F;
                color: #FFFFFF;
                font-weight: 600;
                text-align: left;
                padding: 8px 12px;
            }
            td {
                border: 1px solid #E7E2DA;
                padding: 7px 12px;
                vertical-align: top;
            }
            tr:nth-child(even) td { background: #FDFCFA; }
            tr:hover td { background: #F5F0E8; }
            kbd {
                background: #F5F0E8;
                border: 1px solid #E7E2DA;
                border-radius: 3px;
                padding: 1px 6px;
                font-family: 'Consolas', 'Segoe UI', monospace;
                font-size: 12px;
                white-space: nowrap;
            }
            code {
                background: #F5F0E8;
                padding: 1px 5px;
                border-radius: 3px;
                font-family: 'Consolas', monospace;
                font-size: 12.5px;
            }
            .tip {
                background: #FEF3C7;
                border-left: 4px solid #B8942F;
                padding: 10px 16px;
                margin: 14px 0;
                border-radius: 0 4px 4px 0;
                font-size: 13px;
            }
            .tip-label {
                font-weight: 600;
                color: #B8942F;
            }
            .note {
                background: #EDE7F6;
                border-left: 4px solid #7C3AED;
                padding: 10px 16px;
                margin: 14px 0;
                border-radius: 0 4px 4px 0;
                font-size: 13px;
            }
            .note-label {
                font-weight: 600;
                color: #7C3AED;
            }
            .warn {
                background: #FEE2E2;
                border-left: 4px solid #EF4444;
                padding: 10px 16px;
                margin: 14px 0;
                border-radius: 0 4px 4px 0;
                font-size: 13px;
            }
            .warn-label {
                font-weight: 600;
                color: #EF4444;
            }
            .badge {
                display: inline-block;
                background: #B8942F;
                color: #FFFFFF;
                font-size: 10px;
                font-weight: 600;
                padding: 2px 8px;
                border-radius: 10px;
                vertical-align: middle;
                margin-left: 6px;
            }
            .badge-pro {
                background: #F59E0B;
            }
            .section-divider {
                border: none;
                border-top: 1px solid #E7E2DA;
                margin: 32px 0;
            }
            .hero {
                text-align: center;
                padding: 20px 0 8px 0;
            }
            .hero h1 {
                border: none;
                font-size: 26px;
                margin-bottom: 4px;
            }
            .hero .subtitle {
                color: #57534E;
                font-size: 14px;
            }
            .hero .version {
                color: #B8942F;
                font-size: 13px;
                font-weight: 600;
                margin-top: 4px;
            }
            .feature-grid {
                display: flex;
                flex-wrap: wrap;
                gap: 10px;
                margin: 12px 0;
            }
            .feature-card {
                background: #FAF8F5;
                border: 1px solid #E7E2DA;
                border-radius: 6px;
                padding: 12px 14px;
                width: calc(50% - 5px);
            }
            .feature-card h4 {
                color: #B8942F;
                font-size: 13px;
                font-weight: 600;
                margin-bottom: 4px;
            }
            .feature-card p {
                font-size: 12.5px;
                color: #57534E;
                margin: 0;
            }
            .step {
                display: flex;
                align-items: flex-start;
                margin: 8px 0;
            }
            .step-num {
                background: #B8942F;
                color: #FFFFFF;
                width: 22px;
                height: 22px;
                border-radius: 50%;
                text-align: center;
                line-height: 22px;
                font-size: 12px;
                font-weight: 600;
                flex-shrink: 0;
                margin-right: 10px;
                margin-top: 2px;
            }
            .step-text { flex: 1; }
            </style>
            </head>
            <body>
            """;
    }

    // ==================== Japanese Content ====================

    private static void AppendJapaneseContent(StringBuilder sb)
    {
        var ver = GetVersion();

        // Overview
        sb.Append($"""
            <div class="hero" id="overview">
                <h1>Insight Training Studio</h1>
                <div class="subtitle">教育・研修動画をかんたん作成</div>
                <div class="version">v{ver}</div>
            </div>

            <p>Insight Training Studio は、教育・研修向けのナレーション付き動画を手軽に作成できる Windows デスクトップアプリケーションです。
            PowerPoint や画像・動画素材を取り込み、ナレーション原稿を入力するだけで、VOICEVOX 音声エンジンによる自然な読み上げ付きの動画を自動生成します。</p>

            <div class="feature-grid">
                <div class="feature-card">
                    <h4>かんたん動画作成</h4>
                    <p>素材をドロップしてテキストを入力するだけ。クイックモードなら3ステップで研修動画が完成します。</p>
                </div>
                <div class="feature-card">
                    <h4>3種類の音声エンジン</h4>
                    <p>Edge Neural（高品質）・VOICEVOX（多キャラ）・Windows TTS（オフライン）から選べる柔軟なナレーション。</p>
                </div>
                <div class="feature-card">
                    <h4>字幕・テキスト装飾</h4>
                    <p>10種類以上のプリセットスタイル、レターボックス表示、SRT/VTT 字幕ファイル出力に対応。</p>
                </div>
                <div class="feature-card">
                    <h4>PowerPoint 取り込み & 作成</h4>
                    <p>PPTX 取り込みに加え、DOCX・XLSX・PDF からの変換、AI による PPTX 自動作成にも対応。</p>
                </div>
                <div class="feature-card">
                    <h4>画面キャプチャ・録画</h4>
                    <p>デスクトップ画面をキャプチャ（静止画）・録画（動画）してシーン素材として直接取り込み。</p>
                </div>
                <div class="feature-card">
                    <h4>AI アシスタント</h4>
                    <p>Claude AI がシーン構成・ナレーション・字幕・サムネイルを自動生成。25種類のプリセット搭載。</p>
                </div>
            </div>

            <hr class="section-divider">
            """);

        // Quick Start
        sb.Append("""
            <h1 id="quickstart">クイックスタート</h1>
            <p>初めてお使いの方は、以下の手順で最初の動画を作成してみましょう。</p>

            <h2>動画を作成する（3ステップ）</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>素材を取り込む</strong> — 「企画・制作」タブのドロップゾーンに、画像・動画・PowerPoint ファイルをドラッグ＆ドロップします。
                複数ファイルを一度にドロップすると、ファイルごとにシーンが自動作成されます。
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>ナレーションを設定</strong> — 各シーンのテキスト欄に、読み上げさせたい原稿を入力します。
                話者（ボイス）・話速・向き（横/縦）を設定します。
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>動画を書き出す</strong> — 「動画を生成」ボタンをクリック。保存先を選択すると、ナレーション付き動画が自動で生成されます。
            </div></div>

            <div class="tip">
                <span class="tip-label">ヒント:</span> PowerPoint (.pptx) を取り込むと、スライドが画像として、スピーカーノートがナレーション原稿として自動設定されます。
                既存の研修資料をそのまま動画化できる最も効率的な方法です。
            </div>

            <h2>完成後の操作</h2>
            <ul>
                <li><strong>動画を再生</strong> — 完成した動画をデフォルトプレイヤーで確認</li>
                <li><strong>出力フォルダを開く</strong> — サムネイル (.jpg)・チャプター (.chapters.txt)・メタデータ (.metadata.txt) も同時に出力されます</li>
                <li><strong>設定を変えて再生成</strong> — 話者や向きを変えてワンクリックで再生成</li>
                <li><strong>詳細エディタで編集</strong> — 「動画生成」タブに切り替えて、シーン単位の細かい編集が可能</li>
            </ul>

            <hr class="section-divider">
            """);

        // UI Layout
        sb.Append("""
            <h1 id="ui-layout">画面構成</h1>
            <p>メイン画面は <strong>2つのタブ</strong> で構成されています。目的に合わせてタブを切り替えてお使いください。</p>

            <h2>タイトルバー</h2>
            <p>画面最上部に、以下のボタンが並んでいます。</p>
            <ul>
                <li><strong>新規</strong> — 新しいプロジェクトを作成</li>
                <li><strong>開く</strong> — 保存済みプロジェクト (.icproj) を開く</li>
                <li><strong>保存 / 別名保存</strong> — プロジェクトを保存</li>
                <li><strong>PPTX取込</strong> — PowerPoint ファイルを取り込み</li>
                <li><strong>ライセンス管理</strong> — ライセンスの確認・アクティベート</li>
            </ul>

            <h2>リボンツールバー</h2>
            <p>画面上部のリボンには、教育動画作成のワークフローに沿った4つのグループがあります。</p>
            <table>
                <tr><th>グループ</th><th>機能</th></tr>
                <tr><td><strong>素材・構成</strong></td><td>シーンの追加・削除・並び替え、画面キャプチャ</td></tr>
                <tr><td><strong>制作</strong></td><td>プレビュー、BGM、CTA（行動喚起）、AI実行</td></tr>
                <tr><td><strong>書き出し</strong></td><td>動画書き出し、出力フォルダを開く</td></tr>
                <tr><td><strong>テンプレート</strong></td><td>プロジェクトテンプレート（新規作成時に選択）</td></tr>
            </table>

            <h2>バックステージ（ファイルメニュー）</h2>
            <ul>
                <li><strong>新規</strong> — 新しいプロジェクトを作成</li>
                <li><strong>開く / 最近使ったファイル</strong> — 保存済みプロジェクト (.icproj) を開く</li>
                <li><strong>保存 / 別名保存</strong> — プロジェクトを保存</li>
                <li><strong>PPTX取込</strong> — PowerPoint ファイルを取り込み</li>
                <li><strong>ライセンス管理</strong> — ライセンスの確認・アクティベート</li>
                <li><strong>終了</strong> — アプリケーションを終了</li>
            </ul>

            <h2>「企画・制作」タブ</h2>
            <p>動画の企画段階で使用します。3つのエリアに分かれています。</p>
            <ul>
                <li><strong>クイックセットアップ（左）</strong> — 素材の取り込み、動画の長さ・シーン数の設定</li>
                <li><strong>構成・スクリプト（中央）</strong> — シーン構成の編集、JSON インポート</li>
                <li><strong>サムネイルジェネレーター（右）</strong> — サムネイル画像の作成</li>
            </ul>

            <h2>「動画生成」タブ</h2>
            <p>動画の編集・書き出しを行います。</p>
            <ul>
                <li><strong>シーン一覧（左）</strong> — シーンの追加・削除・並び替え、解像度設定</li>
                <li><strong>シーン編集（中央）</strong> — 素材、ナレーション、字幕、テキストオーバーレイ、トランジション</li>
                <li><strong>書き出し設定（右）</strong> — 話者設定、BGM、書き出し実行</li>
            </ul>

            <h2>ステータスバー</h2>
            <p>画面下部に、VOICEVOX 接続状態・FFmpeg 検出状態・言語切替（JA / EN）が表示されます。</p>

            <hr class="section-divider">
            """);

        // Quick Mode
        sb.Append("""
            <h1 id="quick-mode">クイックモード</h1>
            <p>最短3ステップで動画を作成できる簡易モードです。起動画面から「クイックモード」を選択するか、
            メニューから起動できます。詳細なシーン編集が不要な場合に最適です。</p>

            <h2>使い方</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>ファイルを取り込む</strong> — PPTX・DOCX・XLSX・PDF・画像・動画ファイルをドロップゾーンにドラッグ＆ドロップ
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>設定を選択</strong> — 話者（ボイス）と話速を選択
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>動画を生成</strong> — 「動画を生成」ボタンで書き出し実行
            </div></div>

            <div class="tip">
                <span class="tip-label">ヒント:</span> より細かい編集が必要な場合は、「詳細エディタで開く」ボタンで
                通常モード（メインウィンドウ）に切り替えられます。シーンデータはそのまま引き継がれます。
            </div>

            <h2>対応ファイル形式</h2>
            <table>
                <tr><th>形式</th><th>説明</th></tr>
                <tr><td>PPTX</td><td>スライド→画像、スピーカーノート→ナレーションとして取り込み</td></tr>
                <tr><td>DOCX</td><td>セクション単位でスライドに変換して取り込み</td></tr>
                <tr><td>XLSX</td><td>シート単位でスライドに変換して取り込み</td></tr>
                <tr><td>PDF</td><td>ページ単位でスライドに変換して取り込み</td></tr>
                <tr><td>画像</td><td>PNG, JPG, BMP, GIF をシーン素材として取り込み</td></tr>
                <tr><td>動画</td><td>MP4, AVI, MOV をシーン素材として取り込み</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // Scene Editor
        sb.Append("""
            <h1 id="scene-editor">シーン編集</h1>
            <p>「動画生成」タブでは、動画をシーン単位で構成・編集します。</p>

            <h2>シーンの操作</h2>
            <table>
                <tr><th>操作</th><th>方法</th></tr>
                <tr><td>シーンを追加</td><td>「＋追加」ボタン、または <kbd>Ctrl</kbd>+<kbd>T</kbd></td></tr>
                <tr><td>シーンを削除</td><td>「－削除」ボタン、または <kbd>Delete</kbd>（最低1シーン必要）</td></tr>
                <tr><td>順序を変更</td><td>「↑」「↓」ボタン、または <kbd>Ctrl</kbd>+<kbd>↑</kbd> / <kbd>Ctrl</kbd>+<kbd>↓</kbd></td></tr>
            </table>

            <h2>素材の設定</h2>
            <p>各シーンに画像または動画素材を1つ設定できます。</p>
            <ul>
                <li>「選択」ボタンでファイルを選択（PNG, JPG, BMP, GIF, MP4, AVI, MOV 等）</li>
                <li>プレビュー画像をクリックすると拡大表示</li>
                <li>「クリア」で素材を解除（黒背景になります）</li>
            </ul>
            <p>動画ファイルを素材にした場合、「動画音声を残す」をチェックすると元の動画の音声を保持できます。</p>

            <h2>シーンの長さ</h2>
            <ul>
                <li><strong>自動（推奨）</strong> — ナレーション音声の長さに合わせて自動決定</li>
                <li><strong>固定</strong> — 0.1〜60秒の範囲で指定</li>
            </ul>

            <h2>シーンプレビュー</h2>
            <p>「▶シーンプレビュー」ボタンで、ナレーション・字幕・テキストオーバーレイを反映した状態を動画として確認できます。</p>

            <hr class="section-divider">
            """);

        // Narration
        sb.Append("""
            <h1 id="narration">ナレーション</h1>
            <p>3種類の音声エンジンから選択して、入力したテキストから自然な日本語ナレーションを自動生成します。</p>

            <h2>音声エンジン（TTS）の選択</h2>
            <p>書き出し設定パネルの「音声エンジン」で、使用する TTS エンジンを切り替えられます。</p>
            <table>
                <tr><th>エンジン</th><th>特徴</th><th>要件</th></tr>
                <tr><td><strong>Edge Neural</strong></td><td>Microsoft の高品質ニューラル音声。Nanami（女性）・Keita（男性）等。最も自然な発話。</td><td>インターネット接続が必要</td></tr>
                <tr><td><strong>VOICEVOX</strong></td><td>30種類以上のキャラクターボイス。スタイル（ノーマル、あまあま等）で使い分け可能。</td><td>VOICEVOX エンジンの起動が必要</td></tr>
                <tr><td><strong>Windows TTS</strong></td><td>Windows 標準の OneCore 音声。追加インストール不要でオフラインでも利用可能。</td><td>なし（OS 標準搭載）</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">おすすめ:</span> 品質重視なら Edge Neural、キャラクター性を出したいなら VOICEVOX、
                オフライン環境やシンプルな用途には Windows TTS がおすすめです。デフォルトは Edge Neural です。
            </div>

            <h2>ナレーションの入力</h2>
            <p>シーン編集パネルのテキスト欄に、読み上げさせたい内容を入力してください。
            入力したテキストがそのまま音声合成されてナレーションになります。</p>

            <h2>話者（ボイス）の選択</h2>
            <p>選択した音声エンジンに応じた話者リストから選択できます。</p>
            <ul>
                <li><strong>シーン個別の話者</strong> — 各シーンの「話者」で個別設定（「デフォルト」を選ぶと書き出し設定の話者を使用）</li>
                <li><strong>書き出し設定の話者</strong> — 右パネルの「話者」でプロジェクト全体のデフォルトを設定</li>
            </ul>

            <h2>話速の調整</h2>
            <table>
                <tr><th>設定</th><th>速度</th><th>おすすめの用途</th></tr>
                <tr><td>ゆっくり</td><td>0.8x</td><td>初心者向け研修、丁寧な説明</td></tr>
                <tr><td>標準</td><td>1.0x</td><td>一般的な解説（デフォルト）</td></tr>
                <tr><td>やや速い</td><td>1.2x</td><td>テンポよく進めたい場合</td></tr>
                <tr><td>速い</td><td>1.5x</td><td>短時間に多くの情報を伝えたい場合</td></tr>
            </table>

            <h2>試聴</h2>
            <p>「▶試聴」ボタンで、書き出し前にナレーション音声を確認できます。</p>

            <div class="note">
                <span class="note-label">VOICEVOX エンジンについて:</span>
                VOICEVOX エンジンを使用する場合は、事前にエンジンを起動しておく必要があります。
                初回起動時のセットアップウィザードで接続先を設定します。
                接続が切れた場合は、VOICEVOX を再起動してからアプリを再起動してください。
            </div>

            <hr class="section-divider">
            """);

        // Subtitles & Text
        sb.Append("""
            <h1 id="subtitle-text">字幕・テキスト</h1>

            <h2>字幕</h2>
            <p>各シーンに字幕テキストを設定できます。字幕は動画の下部に表示されます。
            空欄の場合、字幕は表示されません。</p>

            <h3>字幕スタイル</h3>
            <p>「選択...」ボタンで字幕スタイル設定ダイアログが開きます。10種類以上のプリセットから選べます。</p>
            <table>
                <tr><th>スタイル</th><th>特徴</th><th>おすすめの用途</th></tr>
                <tr><td>デフォルト</td><td>白文字に黒縁取り</td><td>汎用</td></tr>
                <tr><td>教育・解説風</td><td>わかりやすく読みやすい</td><td>研修動画、eラーニング</td></tr>
                <tr><td>ニュース風</td><td>ニュース番組のテロップ風</td><td>社内報、お知らせ</td></tr>
                <tr><td>ドキュメンタリー風</td><td>落ち着いた情報番組風</td><td>事例紹介、レポート</td></tr>
                <tr><td>映画風</td><td>エレガントなスタイル</td><td>ブランド紹介</td></tr>
                <tr><td>テック風</td><td>モダンなデザイン</td><td>IT系研修、技術解説</td></tr>
            </table>

            <h3>カスタム設定</h3>
            <p>プリセットをベースに、フォント・サイズ・文字色・縁取り色・縁取り幅を個別にカスタマイズできます。</p>

            <h3>字幕レターボックス</h3>
            <p>「字幕レターボックス」を有効にすると、映像を上部に縮小表示し、下部に黒帯の字幕専用エリアを確保します。
            スライドの内容が字幕で隠れるのを防ぎたい場合に有効です。書き出し設定パネルから切り替えられます。</p>

            <h2>テキストオーバーレイ</h2>
            <p>字幕とは別に、画面上の任意の位置にテキストを配置できます。
            タイトル、見出し、注釈、ポイントの強調などに活用してください。</p>

            <h3>テキストオーバーレイの設定項目</h3>
            <table>
                <tr><th>項目</th><th>説明</th></tr>
                <tr><td>テキスト</td><td>表示する文字列</td></tr>
                <tr><td>横位置 (%)</td><td>画面左端からの水平位置（0〜100%）</td></tr>
                <tr><td>縦位置 (%)</td><td>画面上端からの垂直位置（0〜100%）</td></tr>
                <tr><td>文字サイズ</td><td>フォントサイズ</td></tr>
                <tr><td>揃え</td><td>左揃え・中央揃え・右揃え</td></tr>
                <tr><td>文字色</td><td>テキストの色</td></tr>
                <tr><td>不透明度</td><td>0%（透明）〜100%（不透明）</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // BGM & Effects
        sb.Append("""
            <h1 id="bgm-effects">BGM・エフェクト</h1>

            <h2>BGM（バックグラウンドミュージック）</h2>
            <p>書き出し設定パネルの「BGM設定...」ボタンから設定できます。
            対応形式: MP3, WAV, OGG, M4A, AAC, FLAC, WMA</p>

            <h3>音量設定</h3>
            <table>
                <tr><th>設定</th><th>説明</th></tr>
                <tr><td>メイン音量</td><td>BGMの基本音量</td></tr>
                <tr><td>ダッキング</td><td>ナレーション再生中にBGM音量を自動で下げる</td></tr>
                <tr><td>ダッキング音量</td><td>ナレーション中のBGM音量</td></tr>
                <tr><td>アタック / リリース</td><td>ダッキングの開始・終了にかかる時間</td></tr>
            </table>

            <h3>フェード・ループ</h3>
            <ul>
                <li><strong>フェードイン</strong> — BGMの開始を徐々に音量を上げて自然に</li>
                <li><strong>フェードアウト</strong> — BGMの終了を徐々に音量を下げて自然に</li>
                <li><strong>ループ再生</strong> — 動画がBGMより長い場合、BGMを繰り返し再生</li>
            </ul>

            <h2>トランジション（シーン切り替え効果）</h2>
            <table>
                <tr><th>効果</th><th>説明</th></tr>
                <tr><td>なし</td><td>瞬時に切り替え</td></tr>
                <tr><td>フェード</td><td>フェードイン・フェードアウト</td></tr>
                <tr><td>ディゾルブ</td><td>前後のシーンが重なって切り替わる</td></tr>
                <tr><td>ワイプ（左/右）</td><td>画面を拭き取るように切り替え</td></tr>
                <tr><td>スライド（左/右）</td><td>画面がスライドして切り替え</td></tr>
                <tr><td>ズームイン</td><td>ズームしながら切り替え</td></tr>
            </table>
            <p>切り替え時間は 0.2〜2.0 秒で調整できます。</p>

            <h2>ロゴ透かし（ウォーターマーク）</h2>
            <p>動画に会社ロゴやブランド画像を重ねて表示できます。
            表示位置は左上・右上・左下・右下・中央から選択可能です。</p>

            <hr class="section-divider">
            """);

        // Motion Effects
        sb.Append("""
            <h1 id="motion-effects">モーションエフェクト</h1>
            <p>静止画シーンに動き（パン・ズーム）を付けて、映像にダイナミックさを加える機能です。
            動画素材には適用されません。</p>

            <h2>モーションの種類</h2>
            <table>
                <tr><th>タイプ</th><th>動き</th></tr>
                <tr><td>ズームイン</td><td>画面をゆっくり拡大</td></tr>
                <tr><td>ズームアウト</td><td>画面をゆっくり縮小</td></tr>
                <tr><td>パン左</td><td>画面を左にゆっくりスライド</td></tr>
                <tr><td>パン右</td><td>画面を右にゆっくりスライド</td></tr>
                <tr><td>パン上</td><td>画面を上にゆっくりスライド</td></tr>
                <tr><td>パン下</td><td>画面を下にゆっくりスライド</td></tr>
                <tr><td>なし</td><td>モーションなし（静止）</td></tr>
                <tr><td>自動</td><td>AI が最適なモーションを自動選択</td></tr>
            </table>

            <h2>バリエーションレベル</h2>
            <p>「自動」を選択した場合、以下の3段階からモーションの激しさを選べます。</p>
            <table>
                <tr><th>レベル</th><th>特徴</th></tr>
                <tr><td>Calm（控えめ）</td><td>ズーム中心の落ち着いた動き</td></tr>
                <tr><td>Balanced（バランス）</td><td>ズームとパンを適度に組み合わせ</td></tr>
                <tr><td>Dynamic（ダイナミック）</td><td>多彩なパン・ズームで躍動感のある映像</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">ヒント:</span> 自動モードでは、同じ軸のパンが連続しないよう自動調整されます。
                最終シーンはズームアウトがデフォルトで適用され、自然なエンディングになります。
            </div>

            <hr class="section-divider">
            """);

        // Screen Capture & Recording
        sb.Append("""
            <h1 id="screen-tools">画面キャプチャ・画面録画</h1>
            <p>デスクトップ画面をキャプチャ（静止画）または録画（動画）して、シーンの素材として直接取り込む機能です。
            リボンの「素材・構成」グループからアクセスできます。</p>

            <h2>画面キャプチャ（静止画）</h2>
            <ol>
                <li>リボンの「画面キャプチャ」ボタンをクリック</li>
                <li>キャプチャしたい範囲をマウスでドラッグして選択</li>
                <li>選択範囲が PNG 画像としてキャプチャされ、現在のシーンの素材に設定されます</li>
            </ol>

            <h2>画面録画（動画）</h2>
            <ol>
                <li>リボンの「画面録画」ボタンをクリック</li>
                <li>録画範囲を選択し、録画を開始</li>
                <li>録画停止後、MP4 動画ファイルが現在のシーンの素材に設定されます</li>
            </ol>

            <div class="tip">
                <span class="tip-label">活用例:</span> ソフトウェアの操作手順を録画してそのまま研修動画に組み込んだり、
                画面のスクリーンショットをキャプチャして解説動画の素材にできます。
            </div>

            <hr class="section-divider">
            """);

        // Planning Tab
        sb.Append("""
            <h1 id="planning">企画・制作タブ</h1>
            <p>動画の構成を事前に計画し、効率的に制作を進めるための機能です。
            IPO（Input → Process → Output）デザインに基づく4パネル構成で、直感的に制作を進められます。</p>

            <h2>4パネル構成（IPO デザイン）</h2>
            <table>
                <tr><th>パネル</th><th>役割</th><th>内容</th></tr>
                <tr><td><strong>① Input（参考資料）</strong></td><td>素材の取り込み</td><td>PPTX・DOCX・XLSX・PDF・画像・動画のドラッグ＆ドロップ、ファイル選択</td></tr>
                <tr><td><strong>② Process（プロンプト）</strong></td><td>AI 指示の編集</td><td>プロンプト一覧、エディタ、フィルタ・検索</td></tr>
                <tr><td><strong>③ Output（成果物）</strong></td><td>結果の確認</td><td>生成されたシーン構成、レポート、アーティファクト</td></tr>
                <tr><td><strong>④ AI Chat（コンシェルジュ）</strong></td><td>AI との対話</td><td>Claude AI チャット、ツール実行結果</td></tr>
            </table>

            <h2>ドキュメント変換</h2>
            <p>PPTX 以外のドキュメントも、自動変換して取り込むことができます。</p>
            <table>
                <tr><th>形式</th><th>変換方法</th></tr>
                <tr><td>DOCX（Word）</td><td>セクション単位で1スライドずつ変換</td></tr>
                <tr><td>XLSX（Excel）</td><td>シート単位で1スライドずつ変換</td></tr>
                <tr><td>PDF</td><td>ページ単位で1スライドずつ変換</td></tr>
            </table>
            <div class="note">
                <span class="note-label">変換について:</span>
                PowerPoint がインストールされている環境では COM オートメーションによる高品質な変換を行います。
                インストールされていない場合は、Open XML 解析によるフォールバック変換を使用します。
            </div>

            <h2>AI によるプロジェクト生成</h2>
            <p>AI アシスタント（画面右上の「AI」ボタン）を使って、テーマや目的を入力するだけでシーン構成・ナレーション原稿を自動生成できます。</p>

            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>AI アシスタントを開く</strong> — 画面右上の「AI」ボタンをクリック
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>プリセットを選択</strong> — 「スライドから教育動画自動作成」などのプリセットを選択
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>実行</strong> — AI がシーン構成・ナレーションを自動生成
            </div></div>

            <div class="note">
                <span class="note-label">AI機能について:</span>
                AI 機能を使用するには、Anthropic (Claude) の API キーが必要です。
                AI パネル右上の 🔑 ボタンから設定してください。
            </div>

            <h2>サムネイルジェネレーター</h2>
            <p>右パネルでサムネイル画像（1280×720px）を作成できます。
            カラーパターンの選択、メインテキスト・サブテキストの設定、背景画像の設定が可能です。</p>

            <hr class="section-divider">
            """);

        // PPTX Creation
        sb.Append("""
            <h1 id="pptx-create">PPTX 作成タブ</h1>
            <p>企画・制作タブでは、AI を活用して PowerPoint プレゼンテーション（PPTX）を自動作成することもできます。
            テーマや目的を入力するだけで、スライド構成・テキスト・レイアウトを含む PPTX ファイルを生成します。</p>

            <h2>PPTX 自動作成の手順</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>参考資料を投入</strong> — 参考にしたいドキュメントやテキストを Input パネルに取り込み
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>プロンプトを選択・編集</strong> — Process パネルで AI への指示を設定
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>AI 実行</strong> — AI がスライド構成を生成し、Output パネルに結果を表示
            </div></div>
            <div class="step"><div class="step-num">4</div><div class="step-text">
                <strong>PPTX 出力</strong> — 生成されたスライド構成から PPTX ファイルを書き出し
            </div></div>

            <div class="tip">
                <span class="tip-label">ヒント:</span> 生成された PPTX はそのまま動画の素材として取り込むこともできます。
                「PPTX 作成 → PPTX 取り込み → 動画生成」の一連のワークフローで、資料作成から動画化まで完結します。
            </div>

            <hr class="section-divider">
            """);

        // Export
        sb.Append("""
            <h1 id="export">書き出し</h1>

            <h2>動画の書き出し</h2>
            <p>「動画生成」タブの右パネルで設定を行い、「動画を書き出し」ボタンで動画を生成します。</p>

            <h3>書き出し設定</h3>
            <table>
                <tr><th>設定</th><th>説明</th></tr>
                <tr><td>音声エンジン</td><td>Edge Neural / VOICEVOX / Windows TTS から選択</td></tr>
                <tr><td>話者</td><td>プロジェクト全体のデフォルト話者</td></tr>
                <tr><td>解像度</td><td>1920×1080（横動画）/ 1080×1920（縦動画）</td></tr>
                <tr><td>FPS（フレームレート）</td><td>動画のフレームレートを設定（デフォルト: 30fps）</td></tr>
                <tr><td>字幕レターボックス</td><td>字幕用の黒帯エリアを確保する</td></tr>
                <tr><td>BGM設定</td><td>バックグラウンドミュージックの設定</td></tr>
            </table>

            <h3>出力ファイル</h3>
            <p>書き出し時に以下のファイルが自動生成されます。</p>
            <table>
                <tr><th>ファイル</th><th>説明</th></tr>
                <tr><td>○○○.mp4</td><td>動画ファイル</td></tr>
                <tr><td>○○○_thumbnail.jpg</td><td>サムネイル画像（1280×720px）</td></tr>
                <tr><td>○○○.chapters.txt</td><td>チャプターファイル</td></tr>
                <tr><td>○○○.metadata.txt</td><td>メタデータ（タイトル案・説明文・タグ候補）</td></tr>
                <tr><td>○○○.srt</td><td>SRT 字幕ファイル（動画プレイヤーで読み込み可能）</td></tr>
                <tr><td>○○○.vtt</td><td>WebVTT 字幕ファイル（Web ブラウザ対応）</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">字幕ファイルについて:</span> SRT/VTT ファイルは、YouTube や Vimeo 等の動画プラットフォームに
                字幕としてアップロードしたり、動画プレイヤーで外部字幕として表示するのに活用できます。
            </div>

            <h2>CTA（行動喚起）エンドカード</h2>
            <p>動画の最後に CTA（Call-to-Action）エンドカードを挿入できます。
            リボンの「制作」グループにある「CTA」ボタンから追加します。</p>
            <table>
                <tr><th>テンプレート</th><th>用途</th></tr>
                <tr><td>登録促進</td><td>チャンネル登録・メルマガ登録の案内</td></tr>
                <tr><td>Webサイト</td><td>Webサイトへの誘導</td></tr>
                <tr><td>ダウンロード</td><td>資料ダウンロードの案内</td></tr>
                <tr><td>お問い合わせ</td><td>問い合わせ先の表示</td></tr>
                <tr><td>教育・相談</td><td>研修・コンサルティングの案内</td></tr>
            </table>

            <h2>プロジェクトの保存</h2>
            <p>プロジェクトは <code>.icproj</code> 形式で保存されます。</p>
            <ul>
                <li><kbd>Ctrl</kbd>+<kbd>S</kbd> — 上書き保存</li>
                <li><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> — 別名保存</li>
            </ul>

            <h3>自動保存</h3>
            <p>プロジェクトは <strong>5分間隔で自動保存</strong> されます。自動保存データは以下の場所に保存されます。</p>
            <pre>%LOCALAPPDATA%\InsightCast\AutoSave\</pre>
            <p>予期しないクラッシュや電源断が発生した場合でも、自動保存データから復旧できます。</p>

            <h2>PPTX取込</h2>
            <p>PowerPoint (.pptx) ファイルを取り込むと、スライドごとにシーンが自動作成されます。
            スライド画像とスピーカーノートがシーンの素材・ナレーションに自動設定されます。</p>

            <h2>テンプレート</h2>
            <p>BGM・透かし・解像度などの設定をテンプレートとして保存・読込できます。
            同じ設定で複数の動画を作成する際に便利です。</p>

            <hr class="section-divider">
            """);

        // Batch Export
        sb.Append("""
            <h1 id="batch-export">バッチエクスポート</h1>
            <p>複数のプロジェクトファイル（.icproj）をまとめて一括書き出しできる機能です。
            大量の動画を効率的に生成する際に活用してください。</p>

            <h2>使い方</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>バッチエクスポートを開く</strong> — メニューからバッチエクスポートダイアログを起動
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>プロジェクトを追加</strong> — 書き出したいプロジェクトファイル（.icproj）を追加
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>設定を確認</strong> — FPS など共通設定を確認
            </div></div>
            <div class="step"><div class="step-num">4</div><div class="step-text">
                <strong>一括書き出し</strong> — 全プロジェクトの動画を順番に自動生成
            </div></div>

            <div class="tip">
                <span class="tip-label">活用例:</span> 同じ研修コースの複数モジュールをそれぞれプロジェクトとして作成し、
                バッチエクスポートで一度に全動画を生成できます。
            </div>

            <hr class="section-divider">
            """);

        // Keyboard Shortcuts
        sb.Append("""
            <h1 id="shortcuts">キーボードショートカット</h1>

            <h2>ファイル操作</h2>
            <table>
                <tr><th>ショートカット</th><th>機能</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>N</kbd></td><td>新規プロジェクト</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>プロジェクトを開く</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>上書き保存</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>名前を付けて保存</td></tr>
            </table>

            <h2>シーン操作</h2>
            <table>
                <tr><th>ショートカット</th><th>機能</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>T</kbd></td><td>シーンを追加</td></tr>
                <tr><td><kbd>Delete</kbd></td><td>シーンを削除</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>↑</kbd></td><td>シーンを上へ移動</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>↓</kbd></td><td>シーンを下へ移動</td></tr>
            </table>

            <h2>表示・その他</h2>
            <table>
                <tr><th>ショートカット</th><th>機能</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>+</kbd></td><td>画面を拡大（ズームイン）</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>-</kbd></td><td>画面を縮小（ズームアウト）</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>0</kbd></td><td>ズームをリセット（100%に戻す）</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Enter</kbd></td><td>AI プロンプトを実行</td></tr>
                <tr><td><kbd>F1</kbd></td><td>ヘルプを表示</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // FAQ
        sb.Append("""
            <h1 id="faq">よくある質問（FAQ）</h1>

            <h2>初期設定</h2>

            <h3>Q: VOICEVOX に接続できません。</h3>
            <p>A: VOICEVOX エンジンが起動しているか確認してください。
            通常 <code>http://127.0.0.1:50021</code> で動作しています。
            VOICEVOX を再起動してからアプリを再起動してください。</p>

            <h3>Q: FFmpeg が見つからないと表示されます。</h3>
            <p>A: FFmpeg は動画生成に必須です。以下のいずれかの方法で導入してください。</p>
            <ul>
                <li>PATH 環境変数に ffmpeg.exe のあるフォルダを追加</li>
                <li>アプリフォルダ内の <code>tools\ffmpeg\bin\</code> に ffmpeg.exe を配置</li>
            </ul>

            <h3>Q: VOICEVOX のポート番号を変更したい。</h3>
            <p>A: VOICEVOX エンジンの起動オプションでポート番号を変更した場合は、
            本製品側の設定でも接続先ポートを合わせてください。デフォルトは <code>50021</code> です。</p>

            <h3>Q: インターネットに接続できない環境で使えますか？</h3>
            <p>A: 音声エンジンを「Windows TTS」に切り替えれば、オフライン環境でも動画生成が可能です。
            ただし AI アシスタント機能と Edge Neural 音声はインターネット接続が必要です。</p>

            <h3>Q: 自動保存データはどこにありますか？</h3>
            <p>A: <code>%LOCALAPPDATA%\InsightCast\AutoSave\</code> に5分間隔で自動保存されます。
            アプリ起動時に自動保存データが検出されると「復元しますか？」ダイアログが表示されます。
            7日以上前の自動保存データは自動的に削除されます。</p>

            <h2>動画作成</h2>

            <h3>Q: 動画の書き出しに失敗します。</h3>
            <p>A: 以下を確認してください。</p>
            <ul>
                <li>FFmpeg が正しくインストールされ、PATH に含まれているか</li>
                <li>VOICEVOX エンジンが起動しているか</li>
                <li>保存先ディスクに十分な空き容量があるか</li>
                <li>保存先パスに日本語や特殊文字が含まれていないか</li>
            </ul>

            <h3>Q: 素材なしでも動画は作れますか？</h3>
            <p>A: はい。素材のないシーンは黒背景で生成されます。
            テキストオーバーレイと字幕を活用すれば、画像なしでも教材を作成できます。</p>

            <h3>Q: PowerPoint を取り込んだら文字が表示されません。</h3>
            <p>A: PPTX取込ではスライドを画像として取り込むため、テキストはそのまま画像に含まれます。
            スピーカーノートに記載した内容がナレーション原稿になります。</p>

            <h3>Q: シーンの表示時間を変更したい。</h3>
            <p>A: シーンの表示時間はナレーション音声の長さに基づいて自動計算されます。
            ナレーションテキストを編集して長さを調整するか、企画タブのシーン秒数設定をご利用ください。</p>

            <h3>Q: 対応している画像形式は？</h3>
            <p>A: JPEG (.jpg/.jpeg)、PNG (.png)、BMP (.bmp)、GIF (.gif) に対応しています。
            推奨は 1920×1080 以上の JPEG または PNG です。</p>

            <h3>Q: BGM の音量を調整したい。</h3>
            <p>A: エクスポート設定パネルで BGM の音量レベルを調整できます。
            ナレーションが聞き取りやすいよう、BGM は控えめの音量がおすすめです。</p>

            <h2>AI アシスタント</h2>

            <h3>Q: AI アシスタントの利用にはお金がかかりますか？</h3>
            <p>A: AI アシスタント機能自体は無料ですが、Anthropic の Claude API キーが必要です。
            API 使用料は Anthropic からお客様に直接請求されます（BYOK 方式）。
            本製品が API 利用料を徴収することはありません。</p>

            <h3>Q: API キーはどこで取得できますか？</h3>
            <p>A: Anthropic のコンソール (<code>console.anthropic.com</code>) でアカウントを作成し、
            API キーを発行してください。キーは <code>sk-ant-</code> で始まります。</p>

            <h3>Q: AI がシーンを直接変更してしまいましたが、元に戻せますか？</h3>
            <p>A: <kbd>Ctrl</kbd>+<kbd>Z</kbd> でアンドゥが可能です。
            AI の自動実行（check モード）は、実行前にプロジェクトを保存しておくことをおすすめします。</p>

            <h3>Q: サムネイル自動生成で背景画像を使うには？</h3>
            <p>A: シーンに画像素材が設定されていれば、AI が自動的に背景画像として活用します。
            プリセット「サムネイル自動生成」を実行すると、AI がシーンの画像を検出して背景に使用します。</p>

            <h2>ライセンス</h2>

            <h3>Q: 字幕やトランジションが使えません。</h3>
            <p>A: TRIAL または BIZ 以上のプランが必要です。「ヘルプ」→「ライセンス管理」からライセンスをアクティベートしてください。</p>

            <h3>Q: ライセンスキーの入力方法は？</h3>
            <p>A: メニュー「ヘルプ」→「ライセンス管理」でメールアドレスとライセンスキーを入力し、
            「アクティベート」をクリックしてください。</p>

            <h3>Q: FREE プランから BIZ にアップグレードする方法は？</h3>
            <p>A: 営業担当またはパートナー（販売代理店）にお問い合わせください。
            ライセンスキーを受け取ったら、「ヘルプ」→「ライセンス管理」でアクティベートしてください。</p>

            <h2>その他</h2>

            <h3>Q: テンプレートはどこに保存されますか？</h3>
            <p>A: <code>%LOCALAPPDATA%\InsightCast\Templates</code> に保存されます。</p>

            <h3>Q: 言語を切り替えたい。</h3>
            <p>A: ステータスバーの言語切替ボタン（JA / EN）をクリックしてください。アプリ全体の表示言語が切り替わります。</p>

            <h3>Q: プロジェクトファイルはどの形式ですか？</h3>
            <p>A: <code>.icproj</code> 形式で保存されます。
            <kbd>Ctrl</kbd>+<kbd>S</kbd> で上書き保存、<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> で名前を付けて保存できます。</p>

            <h3>Q: 動画の書き出しで出力されるファイルは？</h3>
            <p>A: 書き出し時に以下の 4 ファイルが生成されます。</p>
            <ul>
                <li><code>*.mp4</code> — 動画ファイル</li>
                <li><code>*_thumbnail.jpg</code> — サムネイル画像（1280×720）</li>
                <li><code>*.chapters.txt</code> — チャプターマーカー</li>
                <li><code>*.metadata.txt</code> — メタデータ（タイトル・説明・タグ）</li>
            </ul>

            <hr class="section-divider">
            """);

        // License
        sb.Append("""
            <h1 id="license">ライセンス</h1>
            <p>Insight Training Studio は以下の4つのプランで提供されています（全製品 法人向け B2B Only）。</p>

            <h2>プラン比較表</h2>
            <table>
                <tr><th>機能</th><th>FREE</th><th>TRIAL</th><th>BIZ</th><th>ENT</th></tr>
                <tr><td>利用期間</td><td>無期限</td><td>30日間</td><td>365日</td><td>要相談</td></tr>
                <tr><td>動画生成</td><td>○</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>VOICEVOX ナレーション</td><td>○</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>テキストオーバーレイ</td><td>○</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>BGM設定</td><td>○</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>動画書き出し（MP4）</td><td>○</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>字幕表示</td><td>×</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>字幕スタイル選択</td><td>×</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>トランジション効果</td><td>×</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>PPTX取込</td><td>×</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>最大解像度</td><td>1080p</td><td>1080p</td><td>1080p</td><td>4K</td></tr>
                <tr><td>最大ファイルサイズ</td><td>200MB</td><td>200MB</td><td>200MB</td><td>無制限</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">プランについて:</span>
                FREE プランは基本機能を無期限でお使いいただけます。
                TRIAL は全機能を30日間お試しいただける評価版です。
                継続してご利用いただく場合は、BIZ プランへアップグレードしてください。
            </div>

            <h2>ライセンスのアクティベート</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                メニュー「ヘルプ」→「ライセンス管理」を開く
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                購入時に届いたメールアドレスとライセンスキーを入力
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                「アクティベート」ボタンをクリック
            </div></div>

            <div class="note">
                <span class="note-label">ご注意:</span>
                ライセンスキーは Insight Training Studio 専用です。他の HARMONIC insight 製品のキーは使用できません。
            </div>

            <hr class="section-divider">
            """);

        // System Requirements
        sb.Append("""
            <h1 id="system-req">システム要件</h1>
            <table>
                <tr><th>項目</th><th>要件</th></tr>
                <tr><td>OS</td><td>Windows 10 / 11（64bit）</td></tr>
                <tr><td>ランタイム</td><td>.NET 8.0 デスクトップランタイム</td></tr>
                <tr><td>必須ソフトウェア</td><td>VOICEVOX エンジン（ナレーション生成に必要）</td></tr>
                <tr><td>必須ツール</td><td>FFmpeg（動画生成に必要）</td></tr>
                <tr><td>メモリ</td><td>4GB 以上推奨</td></tr>
                <tr><td>ストレージ</td><td>500MB 以上の空き容量（動画出力を含まず）</td></tr>
            </table>

            <h2>VOICEVOX のインストール</h2>
            <p><a href="https://voicevox.hiroshiba.jp/">VOICEVOX 公式サイト</a>からダウンロード・インストールしてください。
            アプリ起動前に VOICEVOX エンジンを起動しておく必要があります。</p>

            <h2>FFmpeg のインストール</h2>
            <p>動画の生成・プレビューに FFmpeg が必要です。
            ffmpeg.exe を PATH 環境変数に追加するか、アプリフォルダ内の <code>tools\ffmpeg\bin\</code> に配置してください。</p>

            <hr class="section-divider">
            """);

        // AI Assistant
        sb.Append("""
            <h1 id="ai-assistant">AIアシスタント</h1>
            <p>Claude AI を活用して、ナレーション作成・字幕編集・動画構成の提案・サムネイル生成などを行えます。
            画面右上のアシスタントボタンでパネルを開きます。</p>

            <h2>基本的な使い方</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>APIキーの設定</strong> — パネル右上の 🔑 ボタンをクリックし、Anthropic の API キーを入力して「設定」をクリックします。
                <br><em>※ キーは DPAPI で暗号化され、お使いの端末にのみ保存されます。外部には送信されません。</em>
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>モデルの選択</strong> — パネル左上のドロップダウンで使用する Claude モデルを選択します。
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>プロンプトの入力</strong> — テキスト欄に質問や指示を入力し、「実行」ボタン（または <kbd>Ctrl</kbd>+<kbd>Enter</kbd>）で送信します。
            </div></div>

            <h2>パネルのボタン</h2>
            <table>
                <tr><th>ボタン</th><th>機能</th></tr>
                <tr><td>?</td><td>ヘルプ — このページの AI アシスタントセクションを開きます</td></tr>
                <tr><td>⧉</td><td>ポップアウト — AI パネルを独立したウィンドウとして表示します</td></tr>
                <tr><td>🔑</td><td>APIキー設定 — Anthropic の API キーを設定・変更します</td></tr>
            </table>

            <h2>プリセットプロンプト</h2>
            <p>開発者が用意した 25 種類のプロンプトテンプレートです。10 カテゴリに分類されており、
            ワンクリックで実行できます。🎯 セクションを展開して表示します。</p>
            <table>
                <tr><th>カテゴリ</th><th>内容</th></tr>
                <tr><td>字幕・翻訳</td><td>ナレーションからの字幕生成、日英翻訳</td></tr>
                <tr><td>ナレーション</td><td>スライドノートからのナレーション生成、校正</td></tr>
                <tr><td>構成アドバイス</td><td>動画構成のチェック、教育効果の評価</td></tr>
                <tr><td>サムネイル</td><td>サムネイル自動生成、タイトル・キャッチコピー提案</td></tr>
                <tr><td>研修・教育</td><td>新人研修、業務手順書変換、安全教育</td></tr>
                <tr><td>ビジネス活用</td><td>製品紹介、社内通知、会議要約</td></tr>
                <tr><td>ワンボタン自動化</td><td>スライドから全自動動画作成、日英バイリンガル</td></tr>
                <tr><td>トーン調整</td><td>簡潔化、フォーマル化、カジュアル化</td></tr>
                <tr><td>マーケティング</td><td>SNSショートスクリプト、エレベーターピッチ</td></tr>
                <tr><td>品質改善</td><td>長さ均一化、読み仮名補正、トーン統一</td></tr>
            </table>
            <ul>
                <li>各プリセットにマウスを合わせると、ツールチップでプロンプト内容がプレビューされます。</li>
                <li>右クリック →「マイプロンプトに保存」で、マイプロンプトにコピーしてカスタマイズできます。</li>
            </ul>

            <h2>実行モード</h2>
            <p>プリセットには 2 つの実行モードがあります。</p>
            <table>
                <tr><th>モード</th><th>動作</th><th>用途</th></tr>
                <tr><td><strong>check</strong>（自動実行）</td><td>AI がツールを使ってシーンのナレーション・字幕・サムネイルなどを直接操作します</td><td>字幕生成、ナレーション設定、トーン調整など</td></tr>
                <tr><td><strong>advice</strong>（アドバイス）</td><td>AI が分析結果やアドバイスをテキストで返します。データは変更されません</td><td>構成チェック、教育効果評価、タイトル提案など</td></tr>
            </table>

            <h2>AI実行（ツール対応プロンプト）</h2>
            <p>リボンの「制作」グループにある<strong>「AI実行」</strong>ボタンから、ツール対応のAIプロンプトを選択して実行できます。
            この機能は、AIが14種類の専用ツールを使ってシーンを自動操作する強力な機能です。</p>

            <h3>使い方</h3>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>AI実行ボタン</strong> — リボンの「制作」グループにある「AI実行」をクリック
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>プロンプト選択</strong> — ダイアログから実行したいプロンプトを選択
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>プロンプト確認</strong> — 「プロンプト確認」ボタンでAIアシスタントパネルに反映され、自動実行開始
            </div></div>

            <h3>利用可能なツール</h3>
            <p>AI はシーンを操作するために以下のツールを使用できます。</p>
            <table>
                <tr><th>ツール</th><th>機能</th></tr>
                <tr><td>get_scenes</td><td>全シーンの情報を取得</td></tr>
                <tr><td>set_multiple_scenes</td><td>複数シーンを一括更新（ナレーション・字幕）</td></tr>
                <tr><td>add_scene</td><td>新しいシーンを追加</td></tr>
                <tr><td>delete_scene</td><td>指定シーンを削除</td></tr>
                <tr><td>reorder_scenes</td><td>シーンの順序を変更</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">自動適用と提案:</span>
                各プロンプトには「自動適用」または「提案」モードが設定されています。
                自動適用モードのプロンプトは、AIが直接シーンを編集します。
                提案モードでは、AIが推奨する内容をテキストで提示し、ユーザーが確認してから適用できます。
            </div>

            <h2>サムネイル自動生成</h2>
            <p>AI が動画の内容を分析し、最適なサムネイル画像（1280×720px）を自動生成します。</p>
            <ol>
                <li>プリセット「🖼️ サムネイル自動生成」をクリック</li>
                <li>AI が動画の内容を分析し、3 パターンのサムネイルを生成</li>
                <li>結果エリアにサムネイルのプレビューが表示されます</li>
                <li>「サムネイルを保存」ボタンで PNG ファイルとして保存</li>
            </ol>
            <p>11 種類のレイアウトパターンと 6 種類のカラースタイルから、AI が内容に合った組み合わせを選択します。
            シーンに画像素材がある場合は、背景画像としても活用されます。</p>

            <h2>マイプロンプト（プロンプトライブラリ）</h2>
            <p>よく使うプロンプトを保存して、いつでも呼び出せる機能です。
            業務で繰り返し使う指示を蓄積・改善していくことで、作業効率が向上します。
            📚 セクションを展開して表示します。</p>

            <h3>プロンプトの保存方法</h3>
            <table>
                <tr><th>方法</th><th>手順</th></tr>
                <tr><td>入力欄から保存</td><td>プロンプトを入力し、入力欄の下の 💾 ボタンをクリック</td></tr>
                <tr><td>プリセットから派生</td><td>プリセットを右クリック →「マイプロンプトに保存」</td></tr>
            </table>

            <h3>プロンプトの管理</h3>
            <p>マイプロンプトのカードを右クリックすると、以下の操作メニューが表示されます。</p>
            <ul>
                <li><strong>実行</strong> — カードをクリックすると、プロンプトが入力欄にロードされます。</li>
                <li><strong>編集</strong> — 右クリック →「編集」で、ラベル・カテゴリ・アイコン・本文を変更できます。</li>
                <li><strong>複製</strong> — 右クリック →「複製」で、既存プロンプトをベースに新しいものを作成できます。</li>
                <li><strong>削除</strong> — 右クリック →「削除」で削除できます（元に戻せません）。</li>
            </ul>

            <h3>データの保存場所</h3>
            <p>マイプロンプトはお使いの端末のローカルフォルダに個別の JSON ファイルとして保存されます。
            クラウドには送信されません。</p>
            <pre>%LOCALAPPDATA%\InsightCast\Prompts\</pre>

            <h2>使用状況の確認</h2>
            <p>API キーを設定すると、パネル上部に使用状況が表示されます。</p>
            <table>
                <tr><th>項目</th><th>説明</th></tr>
                <tr><td>コール数</td><td>API 呼び出し回数</td></tr>
                <tr><td>トークン数</td><td>入力 / 出力トークン数</td></tr>
                <tr><td>概算コスト</td><td>セッション中の推定 API 使用料（USD）</td></tr>
            </table>
            <div class="tip">
                <span class="tip-label">API 利用料:</span>
                API の使用料は Anthropic からお客様のアカウントに直接請求されます（BYOK 方式）。
                本製品は API 使用料を一切徴収しません。
            </div>

            <hr class="section-divider">
            """);

        // Support
        sb.Append("""
            <h1 id="support">お問い合わせ</h1>
            <p>ご不明な点やお困りのことがございましたら、以下よりお問い合わせください。</p>

            <h2>サポート窓口</h2>
            <table>
                <tr><th>項目</th><th>情報</th></tr>
                <tr><td>メール</td><td>support@h-insight.jp</td></tr>
                <tr><td>開発元</td><td>HARMONIC insight</td></tr>
                <tr><td>Web</td><td><a href="https://www.insight-office.com/ja">https://www.insight-office.com/ja</a></td></tr>
            </table>

            <h2>利用規約・プライバシーポリシー</h2>
            <ul>
                <li><a href="https://www.insight-office.com/ja/ja/terms">利用規約</a></li>
                <li><a href="https://www.insight-office.com/ja/ja/privacy">プライバシーポリシー</a></li>
            </ul>

            <h2>VOICEVOX について</h2>
            <p>本製品は VOICEVOX 音声エンジンを利用しています。
            VOICEVOX の利用にあたっては、各キャラクターの利用規約をご確認ください。</p>
            """);
    }

    // ==================== English Content ====================

    private static void AppendEnglishContent(StringBuilder sb)
    {
        var ver = GetVersion();

        // Overview
        sb.Append($"""
            <div class="hero" id="overview">
                <h1>Insight Training Studio</h1>
                <div class="subtitle">Create training and educational videos with ease</div>
                <div class="version">v{ver}</div>
            </div>

            <p>Insight Training Studio is a Windows desktop application for creating narrated educational and training videos.
            Import PowerPoint presentations, images, or video files, enter your narration scripts,
            and the VOICEVOX text-to-speech engine automatically generates natural-sounding narration.</p>

            <div class="feature-grid">
                <div class="feature-card">
                    <h4>Easy Video Creation</h4>
                    <p>Drop media files and enter text. Quick Mode creates training videos in just 3 steps.</p>
                </div>
                <div class="feature-card">
                    <h4>3 TTS Engines</h4>
                    <p>Edge Neural (high quality), VOICEVOX (30+ character voices), and Windows TTS (offline). Choose the best fit.</p>
                </div>
                <div class="feature-card">
                    <h4>Subtitles & Text</h4>
                    <p>10+ preset styles, letterbox mode, and SRT/VTT subtitle file output for video platforms.</p>
                </div>
                <div class="feature-card">
                    <h4>PowerPoint Import & Creation</h4>
                    <p>Import PPTX directly, convert DOCX/XLSX/PDF, or auto-create PPTX with AI assistance.</p>
                </div>
                <div class="feature-card">
                    <h4>Screen Capture & Recording</h4>
                    <p>Capture screenshots or record screen video directly as scene media for software tutorials.</p>
                </div>
                <div class="feature-card">
                    <h4>AI Assistant</h4>
                    <p>Claude AI auto-generates scene structure, narration, subtitles, and thumbnails. 25 preset prompts included.</p>
                </div>
            </div>

            <hr class="section-divider">
            """);

        // Quick Start
        sb.Append("""
            <h1 id="quickstart">Quick Start</h1>
            <p>Follow these steps to create your first video.</p>

            <h2>Create a Video (3 Steps)</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Import media</strong> — Drag & drop images, videos, or PowerPoint files onto the drop zone in the Planning tab.
                Dropping multiple files creates one scene per file automatically.
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Set narration</strong> — Enter the text to be spoken in each scene's text field.
                Configure speaker (voice), speed, and orientation (landscape/portrait).
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Export video</strong> — Click "Generate Video", choose a save location, and the narrated video is generated automatically.
            </div></div>

            <div class="tip">
                <span class="tip-label">Tip:</span> Importing a PowerPoint (.pptx) file automatically converts slides to images and speaker notes to narration scripts.
                This is the most efficient way to turn existing training materials into videos.
            </div>

            <h2>After Completion</h2>
            <ul>
                <li><strong>Play Video</strong> — Preview the completed video in your default player</li>
                <li><strong>Open Output Folder</strong> — View thumbnail (.jpg), chapters (.chapters.txt), and metadata (.metadata.txt)</li>
                <li><strong>Regenerate</strong> — Change speaker or orientation and regenerate with one click</li>
                <li><strong>Edit in Detail</strong> — Switch to the Video Generation tab for scene-level editing</li>
            </ul>

            <hr class="section-divider">
            """);

        // UI Layout
        sb.Append("""
            <h1 id="ui-layout">UI Layout</h1>
            <p>The main window has <strong>two tabs</strong>. Switch between them based on your workflow.</p>

            <h2>Title Bar</h2>
            <ul>
                <li><strong>New</strong> — Create a new project</li>
                <li><strong>Open</strong> — Open a saved project (.icproj)</li>
                <li><strong>Save / Save As</strong> — Save the project</li>
                <li><strong>Import PPTX</strong> — Import a PowerPoint file</li>
                <li><strong>License Manager</strong> — View and activate license</li>
            </ul>

            <h2>Ribbon Toolbar</h2>
            <p>The ribbon at the top of the window has four groups organized by training video workflow.</p>
            <table>
                <tr><th>Group</th><th>Features</th></tr>
                <tr><td><strong>Structure</strong></td><td>Add/remove/reorder scenes, screen capture</td></tr>
                <tr><td><strong>Production</strong></td><td>Preview, BGM, CTA (Call-to-Action), AI Execute</td></tr>
                <tr><td><strong>Export</strong></td><td>Generate video, open output folder</td></tr>
                <tr><td><strong>Templates</strong></td><td>Project templates (available on new project)</td></tr>
            </table>

            <h2>Backstage (File Menu)</h2>
            <ul>
                <li><strong>New</strong> — Create a new project</li>
                <li><strong>Open / Recent Files</strong> — Open a saved project (.icproj)</li>
                <li><strong>Save / Save As</strong> — Save the project</li>
                <li><strong>Import PPTX</strong> — Import a PowerPoint file</li>
                <li><strong>License Manager</strong> — View and activate license</li>
                <li><strong>Exit</strong> — Close the application</li>
            </ul>

            <h2>Planning Tab</h2>
            <ul>
                <li><strong>Quick Setup (Left)</strong> — Import media, set duration and scene count</li>
                <li><strong>Structure & Script (Center)</strong> — Edit scene structure, JSON import</li>
                <li><strong>Thumbnail Generator (Right)</strong> — Create thumbnail images</li>
            </ul>

            <h2>Video Generation Tab</h2>
            <ul>
                <li><strong>Scene List (Left)</strong> — Add, remove, reorder scenes; set resolution</li>
                <li><strong>Scene Editor (Center)</strong> — Media, narration, subtitles, overlays, transitions</li>
                <li><strong>Export Settings (Right)</strong> — Speaker, BGM, export</li>
            </ul>

            <h2>Status Bar</h2>
            <p>Shows VOICEVOX connection status, FFmpeg detection status, and language toggle (JA/EN).</p>

            <hr class="section-divider">
            """);

        // Quick Mode
        sb.Append("""
            <h1 id="quick-mode">Quick Mode</h1>
            <p>A simplified mode that creates videos in just 3 steps. Launch from the startup screen or menu.
            Ideal when detailed scene editing is not needed.</p>

            <h2>How to Use</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Import files</strong> — Drag & drop PPTX, DOCX, XLSX, PDF, image, or video files onto the drop zone
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Choose settings</strong> — Select speaker (voice) and speech speed
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Generate video</strong> — Click "Generate Video" to export
            </div></div>

            <div class="tip">
                <span class="tip-label">Tip:</span> For more detailed editing, click "Open in Detail Editor" to switch to the
                full editor (main window). Scene data is carried over seamlessly.
            </div>

            <h2>Supported File Formats</h2>
            <table>
                <tr><th>Format</th><th>Description</th></tr>
                <tr><td>PPTX</td><td>Slides become images, speaker notes become narration</td></tr>
                <tr><td>DOCX</td><td>Sections are converted to individual slides</td></tr>
                <tr><td>XLSX</td><td>Sheets are converted to individual slides</td></tr>
                <tr><td>PDF</td><td>Pages are converted to individual slides</td></tr>
                <tr><td>Images</td><td>PNG, JPG, BMP, GIF imported as scene media</td></tr>
                <tr><td>Videos</td><td>MP4, AVI, MOV imported as scene media</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // Scene Editor
        sb.Append("""
            <h1 id="scene-editor">Scene Editor</h1>
            <p>In the Video Generation tab, compose and edit your video scene by scene.</p>

            <h2>Scene Operations</h2>
            <table>
                <tr><th>Action</th><th>How</th></tr>
                <tr><td>Add Scene</td><td>"+ Add" button or <kbd>Ctrl</kbd>+<kbd>T</kbd></td></tr>
                <tr><td>Remove Scene</td><td>"- Remove" button or <kbd>Delete</kbd> (minimum 1 scene)</td></tr>
                <tr><td>Reorder</td><td>Arrow buttons or <kbd>Ctrl</kbd>+<kbd>Up</kbd> / <kbd>Ctrl</kbd>+<kbd>Down</kbd></td></tr>
            </table>

            <h2>Setting Media</h2>
            <p>Each scene can have one image or video file. Supported formats: PNG, JPG, BMP, GIF, MP4, AVI, MOV, etc.</p>
            <p>When using a video file, check "Keep original audio" to preserve the video's audio alongside narration.</p>

            <h2>Scene Duration</h2>
            <ul>
                <li><strong>Auto (Recommended)</strong> — Duration matches narration length</li>
                <li><strong>Fixed</strong> — Set a specific duration (0.1 to 60 seconds)</li>
            </ul>

            <hr class="section-divider">
            """);

        // Narration
        sb.Append("""
            <h1 id="narration">Narration</h1>
            <p>Automatically generate natural Japanese narration from text using one of three TTS (text-to-speech) engines.</p>

            <h2>TTS Engine Selection</h2>
            <p>Switch engines in the "TTS Engine" dropdown in the export settings panel.</p>
            <table>
                <tr><th>Engine</th><th>Features</th><th>Requirements</th></tr>
                <tr><td><strong>Edge Neural</strong></td><td>High-quality Microsoft neural voices. Nanami (female), Keita (male), etc. Most natural speech.</td><td>Internet connection required</td></tr>
                <tr><td><strong>VOICEVOX</strong></td><td>30+ character voices with styles (Normal, Sweet, etc.). Great for personality.</td><td>VOICEVOX engine must be running</td></tr>
                <tr><td><strong>Windows TTS</strong></td><td>Windows built-in OneCore voices. No extra install, works offline.</td><td>None (built into OS)</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">Recommendation:</span> Edge Neural for quality, VOICEVOX for character variety,
                Windows TTS for offline or simple use. Default is Edge Neural.
            </div>

            <h2>Entering Narration</h2>
            <p>Type the text to be spoken in the scene editor's text field. The text is synthesized into speech for narration.</p>

            <h2>Speaker Selection</h2>
            <ul>
                <li><strong>Per-scene speaker</strong> — Set in each scene's "Speaker" dropdown ("Default" uses project settings)</li>
                <li><strong>Export speaker</strong> — Set the project-wide default in the export panel</li>
            </ul>

            <h2>Speech Speed</h2>
            <table>
                <tr><th>Setting</th><th>Speed</th><th>Best For</th></tr>
                <tr><td>Slow</td><td>0.8x</td><td>Beginner training, detailed explanations</td></tr>
                <tr><td>Normal</td><td>1.0x</td><td>Standard pace (default)</td></tr>
                <tr><td>Slightly Fast</td><td>1.2x</td><td>Brisk delivery</td></tr>
                <tr><td>Fast</td><td>1.5x</td><td>Information-dense content</td></tr>
            </table>

            <div class="note">
                <span class="note-label">About VOICEVOX:</span>
                When using the VOICEVOX engine, it must be running before generating narration.
                Configure the connection in the setup wizard on first launch.
            </div>

            <hr class="section-divider">
            """);

        // Subtitles & Text
        sb.Append("""
            <h1 id="subtitle-text">Subtitles & Text</h1>

            <h2>Subtitles</h2>
            <p>Add subtitle text to each scene. Subtitles appear at the bottom of the video.
            Choose from 10+ preset styles including Default, Education, News, Documentary, Cinema, and Tech.</p>

            <h3>Subtitle Letterbox</h3>
            <p>Enable "Subtitle Letterbox" to shrink the video frame to the top portion and display subtitles
            in a dedicated black bar below. This prevents subtitles from overlapping slide content.
            Toggle in the export settings panel.</p>

            <h2>Text Overlays</h2>
            <p>Place text anywhere on screen for titles, headings, annotations, or emphasis.
            Configure text, position, size, alignment, color, and opacity.</p>

            <hr class="section-divider">
            """);

        // BGM & Effects
        sb.Append("""
            <h1 id="bgm-effects">BGM & Effects</h1>

            <h2>BGM (Background Music)</h2>
            <p>Configure BGM via "BGM Settings..." in the export panel. Supports MP3, WAV, OGG, M4A, AAC, FLAC, WMA.
            Features include volume control, ducking (auto-lower during narration), fade in/out, and loop playback.</p>

            <h2>Transitions</h2>
            <table>
                <tr><th>Effect</th><th>Description</th></tr>
                <tr><td>None</td><td>Instant switch</td></tr>
                <tr><td>Fade</td><td>Fade in/out</td></tr>
                <tr><td>Dissolve</td><td>Cross-dissolve between scenes</td></tr>
                <tr><td>Wipe (L/R)</td><td>Wipe transition</td></tr>
                <tr><td>Slide (L/R)</td><td>Slide transition</td></tr>
                <tr><td>Zoom In</td><td>Zoom transition</td></tr>
            </table>

            <h2>Logo Watermark</h2>
            <p>Overlay a company logo or brand image. Position: Top-Left, Top-Right, Bottom-Left, Bottom-Right, or Center.</p>

            <hr class="section-divider">
            """);

        // Motion Effects
        sb.Append("""
            <h1 id="motion-effects">Motion Effects</h1>
            <p>Add pan and zoom motion to still-image scenes for a more dynamic video. Not applied to video media.</p>

            <h2>Motion Types</h2>
            <table>
                <tr><th>Type</th><th>Motion</th></tr>
                <tr><td>Zoom In</td><td>Slowly zoom into the image</td></tr>
                <tr><td>Zoom Out</td><td>Slowly zoom out of the image</td></tr>
                <tr><td>Pan Left</td><td>Slowly slide the image left</td></tr>
                <tr><td>Pan Right</td><td>Slowly slide the image right</td></tr>
                <tr><td>Pan Up</td><td>Slowly slide the image up</td></tr>
                <tr><td>Pan Down</td><td>Slowly slide the image down</td></tr>
                <tr><td>None</td><td>No motion (static)</td></tr>
                <tr><td>Auto</td><td>AI selects optimal motion automatically</td></tr>
            </table>

            <h2>Variation Levels</h2>
            <p>When set to "Auto", choose from three intensity levels:</p>
            <table>
                <tr><th>Level</th><th>Characteristics</th></tr>
                <tr><td>Calm</td><td>Gentle zoom-focused movements</td></tr>
                <tr><td>Balanced</td><td>Mix of zoom and pan effects</td></tr>
                <tr><td>Dynamic</td><td>Varied pan and zoom for energetic visuals</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">Tip:</span> In auto mode, consecutive same-axis pans are avoided for visual variety.
                The last scene defaults to Zoom Out for a natural ending.
            </div>

            <hr class="section-divider">
            """);

        // Screen Capture & Recording
        sb.Append("""
            <h1 id="screen-tools">Screen Capture & Recording</h1>
            <p>Capture screenshots (still images) or record screen video (MP4) and import directly as scene media.
            Access from the Structure group in the ribbon.</p>

            <h2>Screen Capture (Still Image)</h2>
            <ol>
                <li>Click "Screen Capture" in the ribbon</li>
                <li>Drag to select the capture area</li>
                <li>The selected area is captured as PNG and set as the current scene's media</li>
            </ol>

            <h2>Screen Recording (Video)</h2>
            <ol>
                <li>Click "Screen Recording" in the ribbon</li>
                <li>Select the recording area and start recording</li>
                <li>After stopping, the MP4 video is set as the current scene's media</li>
            </ol>

            <div class="tip">
                <span class="tip-label">Use Case:</span> Record software tutorials and directly embed them in training videos,
                or capture screenshots as visual aids for instructional content.
            </div>

            <hr class="section-divider">
            """);

        // Planning Tab
        sb.Append("""
            <h1 id="planning">Planning Tab</h1>
            <p>Plan your video structure before editing for efficient production.
            The tab uses an IPO (Input → Process → Output) design with 4 panels for intuitive workflow.</p>

            <h2>4-Panel Layout (IPO Design)</h2>
            <table>
                <tr><th>Panel</th><th>Role</th><th>Content</th></tr>
                <tr><td><strong>① Input (Reference)</strong></td><td>Import materials</td><td>Drag & drop PPTX, DOCX, XLSX, PDF, images, videos</td></tr>
                <tr><td><strong>② Process (Prompts)</strong></td><td>Edit AI instructions</td><td>Prompt list, editor, filter & search</td></tr>
                <tr><td><strong>③ Output (Artifacts)</strong></td><td>View results</td><td>Generated scene structure, reports, artifacts</td></tr>
                <tr><td><strong>④ AI Chat (Concierge)</strong></td><td>AI conversation</td><td>Claude AI chat, tool execution results</td></tr>
            </table>

            <h2>Document Conversion</h2>
            <p>Documents other than PPTX can be automatically converted and imported.</p>
            <table>
                <tr><th>Format</th><th>Conversion Method</th></tr>
                <tr><td>DOCX (Word)</td><td>One slide per section</td></tr>
                <tr><td>XLSX (Excel)</td><td>One slide per sheet</td></tr>
                <tr><td>PDF</td><td>One slide per page</td></tr>
            </table>
            <div class="note">
                <span class="note-label">Note:</span>
                When PowerPoint is installed, high-quality COM automation conversion is used.
                Otherwise, Open XML parsing fallback is used.
            </div>

            <h2>AI Project Generation</h2>
            <p>Use the AI Assistant (click the "AI" button at top-right) to automatically create scene structures and narration scripts from a theme or topic.</p>

            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Open AI Assistant</strong> — Click the "AI" button at top-right
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Select a preset</strong> — Choose "Auto-create training video from slides" or similar preset
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Execute</strong> — AI automatically generates scene structure and narration
            </div></div>

            <div class="note">
                <span class="note-label">Note:</span>
                AI features require an Anthropic (Claude) API key.
                Click the 🔑 button at the top-right of the AI panel to configure it.
            </div>

            <h2>Thumbnail Generator</h2>
            <p>Create thumbnail images (1280x720px) with color patterns, text, and background images.</p>

            <hr class="section-divider">
            """);

        // PPTX Creation
        sb.Append("""
            <h1 id="pptx-create">PPTX Creation</h1>
            <p>The Planning tab also supports AI-powered automatic creation of PowerPoint presentations (PPTX).
            Enter a theme or topic and AI generates slides with structure, text, and layout.</p>

            <h2>Steps</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Add reference materials</strong> — Import documents or text into the Input panel
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Select/edit prompt</strong> — Configure AI instructions in the Process panel
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Execute AI</strong> — AI generates slide structure, shown in the Output panel
            </div></div>
            <div class="step"><div class="step-num">4</div><div class="step-text">
                <strong>Export PPTX</strong> — Save the generated structure as a PPTX file
            </div></div>

            <div class="tip">
                <span class="tip-label">Tip:</span> Generated PPTX files can be directly imported as video source material.
                The workflow "Create PPTX → Import PPTX → Generate Video" completes the entire pipeline.
            </div>

            <hr class="section-divider">
            """);

        // Export
        sb.Append("""
            <h1 id="export">Export</h1>

            <h2>Video Export</h2>
            <p>Configure settings in the right panel and click "Export Video".</p>

            <h3>Export Settings</h3>
            <table>
                <tr><th>Setting</th><th>Description</th></tr>
                <tr><td>TTS Engine</td><td>Edge Neural / VOICEVOX / Windows TTS</td></tr>
                <tr><td>Speaker</td><td>Project-wide default speaker</td></tr>
                <tr><td>Resolution</td><td>1920×1080 (landscape) / 1080×1920 (portrait)</td></tr>
                <tr><td>FPS (Frame Rate)</td><td>Video frame rate (default: 30fps)</td></tr>
                <tr><td>Subtitle Letterbox</td><td>Reserve a black bar area for subtitles</td></tr>
                <tr><td>BGM Settings</td><td>Background music configuration</td></tr>
            </table>

            <h3>Output Files</h3>
            <p>Export generates the following files:</p>
            <table>
                <tr><th>File</th><th>Description</th></tr>
                <tr><td>*.mp4</td><td>Video file</td></tr>
                <tr><td>*_thumbnail.jpg</td><td>Thumbnail image (1280x720)</td></tr>
                <tr><td>*.chapters.txt</td><td>Chapter markers</td></tr>
                <tr><td>*.metadata.txt</td><td>Metadata (title, description, tags)</td></tr>
                <tr><td>*.srt</td><td>SRT subtitle file (for video players)</td></tr>
                <tr><td>*.vtt</td><td>WebVTT subtitle file (for web browsers)</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">Subtitle Files:</span> SRT/VTT files can be uploaded to YouTube, Vimeo, and other
                video platforms as captions, or used as external subtitles in video players.
            </div>

            <h2>CTA (Call-to-Action) Endcard</h2>
            <p>Insert a CTA endcard at the end of your video. Access via the "CTA" button in the Production ribbon group.</p>
            <table>
                <tr><th>Template</th><th>Use Case</th></tr>
                <tr><td>Subscribe</td><td>Channel/newsletter subscription prompt</td></tr>
                <tr><td>Website</td><td>Website visit prompt</td></tr>
                <tr><td>Download</td><td>Resource download prompt</td></tr>
                <tr><td>Contact</td><td>Contact information display</td></tr>
                <tr><td>Education</td><td>Training/consulting promotion</td></tr>
            </table>

            <h2>Project Files</h2>
            <p>Projects are saved as <code>.icproj</code> files.
            <kbd>Ctrl</kbd>+<kbd>S</kbd> to save, <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> to save as.</p>

            <h3>Auto-Save</h3>
            <p>Projects are <strong>auto-saved every 5 minutes</strong> to:</p>
            <pre>%LOCALAPPDATA%\InsightCast\AutoSave\</pre>
            <p>Use auto-save data to recover from unexpected crashes or power loss.</p>

            <h2>PPTX Import</h2>
            <p>Import PowerPoint (.pptx) to auto-create scenes from slides. Speaker notes become narration scripts.</p>

            <h2>Templates</h2>
            <p>Save and load settings (BGM, watermark, resolution) as templates for consistent video production.</p>

            <hr class="section-divider">
            """);

        // Batch Export
        sb.Append("""
            <h1 id="batch-export">Batch Export</h1>
            <p>Export multiple project files (.icproj) in a single batch operation.
            Useful for generating a large number of videos efficiently.</p>

            <h2>How to Use</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Open Batch Export</strong> — Launch the batch export dialog from the menu
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Add projects</strong> — Add the project files (.icproj) you want to export
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Review settings</strong> — Confirm common settings like FPS
            </div></div>
            <div class="step"><div class="step-num">4</div><div class="step-text">
                <strong>Batch export</strong> — All project videos are generated sequentially
            </div></div>

            <div class="tip">
                <span class="tip-label">Use Case:</span> Create each module of a training course as a separate project,
                then batch export all videos at once.
            </div>

            <hr class="section-divider">
            """);

        // Keyboard Shortcuts
        sb.Append("""
            <h1 id="shortcuts">Keyboard Shortcuts</h1>

            <h2>File Operations</h2>
            <table>
                <tr><th>Shortcut</th><th>Action</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>N</kbd></td><td>New Project</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>Open Project</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>Save</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>Save As</td></tr>
            </table>

            <h2>Scene Operations</h2>
            <table>
                <tr><th>Shortcut</th><th>Action</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>T</kbd></td><td>Add Scene</td></tr>
                <tr><td><kbd>Delete</kbd></td><td>Remove Scene</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Up</kbd></td><td>Move Scene Up</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Down</kbd></td><td>Move Scene Down</td></tr>
            </table>

            <h2>View & Other</h2>
            <table>
                <tr><th>Shortcut</th><th>Action</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>+</kbd></td><td>Zoom In (enlarge UI)</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>-</kbd></td><td>Zoom Out (shrink UI)</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>0</kbd></td><td>Reset Zoom (back to 100%)</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Enter</kbd></td><td>Execute AI Prompt</td></tr>
                <tr><td><kbd>F1</kbd></td><td>Show Help</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // FAQ
        sb.Append("""
            <h1 id="faq">FAQ</h1>

            <h2>Setup</h2>

            <h3>Q: Cannot connect to VOICEVOX.</h3>
            <p>A: Ensure the VOICEVOX engine is running (typically at <code>http://127.0.0.1:50021</code>).
            Restart VOICEVOX, then restart the app.</p>

            <h3>Q: FFmpeg not found.</h3>
            <p>A: FFmpeg is required for video generation. Add ffmpeg.exe to your PATH or place it in <code>tools\ffmpeg\bin\</code> within the app folder.</p>

            <h3>Q: How do I change the VOICEVOX port?</h3>
            <p>A: If you changed the VOICEVOX engine port in its startup options, make sure to update the connection port in this app's settings accordingly. The default is <code>50021</code>.</p>

            <h3>Q: Can I use this offline?</h3>
            <p>A: Switch the TTS engine to "Windows TTS" for offline video generation.
            However, AI Assistant features and Edge Neural voices require internet access.</p>

            <h3>Q: Where is auto-save data stored?</h3>
            <p>A: Auto-save data is stored in <code>%LOCALAPPDATA%\InsightCast\AutoSave\</code> every 5 minutes.
            On startup, if auto-save data is detected, a "Restore?" dialog will appear.
            Auto-save data older than 7 days is automatically removed.</p>

            <h2>Video Creation</h2>

            <h3>Q: Video export fails.</h3>
            <p>A: Check the following:</p>
            <ul>
                <li>FFmpeg is installed and in your PATH</li>
                <li>VOICEVOX engine is running</li>
                <li>Enough free disk space at the save location</li>
                <li>Save path does not contain special or non-ASCII characters</li>
            </ul>

            <h3>Q: Can I create a video without media?</h3>
            <p>A: Yes. Scenes without media use a black background. Combine with text overlays and subtitles to create content.</p>

            <h3>Q: How do I change scene duration?</h3>
            <p>A: Scene duration is automatically calculated based on narration audio length. Adjust by editing narration text or use the scene duration settings in the Planning tab.</p>

            <h3>Q: What image formats are supported?</h3>
            <p>A: JPEG (.jpg/.jpeg), PNG (.png), BMP (.bmp), and GIF (.gif). JPEG or PNG at 1920×1080 or above is recommended.</p>

            <h3>Q: How do I adjust BGM volume?</h3>
            <p>A: BGM volume can be adjusted in the export settings panel. A lower BGM volume is recommended to keep narration clearly audible.</p>

            <h2>AI Assistant</h2>

            <h3>Q: Does the AI Assistant cost money?</h3>
            <p>A: The AI Assistant feature itself is free, but requires an Anthropic Claude API key. API usage is billed directly by Anthropic to your account (BYOK model). This product does not charge any API fees.</p>

            <h3>Q: Where can I get an API key?</h3>
            <p>A: Create an account at the Anthropic console (<code>console.anthropic.com</code>) and generate an API key. Keys start with <code>sk-ant-</code>.</p>

            <h3>Q: AI directly modified my scenes — can I undo?</h3>
            <p>A: Use <kbd>Ctrl</kbd>+<kbd>Z</kbd> to undo. We recommend saving your project before running AI auto-execution presets.</p>

            <h3>Q: How do I use a background image for thumbnail generation?</h3>
            <p>A: If a scene has image media, AI will automatically detect and use it as a thumbnail background. Run the "Auto-generate thumbnails" preset and AI will find suitable scene images.</p>

            <h2>License</h2>

            <h3>Q: Subtitles and transitions are not available.</h3>
            <p>A: These require a TRIAL or BIZ plan or above. Go to Help &gt; License Manager to activate your license.</p>

            <h3>Q: How to enter a license key?</h3>
            <p>A: Go to Help &gt; License Manager, enter your email and key, then click "Activate".</p>

            <h3>Q: How do I upgrade from FREE to BIZ?</h3>
            <p>A: Contact your sales representative or partner (reseller). Once you receive a license key, activate it via Help &gt; License Manager.</p>

            <h2>Other</h2>

            <h3>Q: Where are templates saved?</h3>
            <p>A: Templates are saved in <code>%LOCALAPPDATA%\InsightCast\Templates</code>.</p>

            <h3>Q: How do I switch languages?</h3>
            <p>A: Click the language toggle button (JA / EN) in the status bar. The entire app UI language will switch.</p>

            <h3>Q: What is the project file format?</h3>
            <p>A: Projects are saved as <code>.icproj</code> files.
            Use <kbd>Ctrl</kbd>+<kbd>S</kbd> to save or <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> to save as.</p>

            <h3>Q: What files are created during video export?</h3>
            <p>A: Video export generates 4 files:</p>
            <ul>
                <li><code>*.mp4</code> — Video file</li>
                <li><code>*_thumbnail.jpg</code> — Thumbnail image (1280×720)</li>
                <li><code>*.chapters.txt</code> — Chapter markers</li>
                <li><code>*.metadata.txt</code> — Metadata (title, description, tags)</li>
            </ul>

            <hr class="section-divider">
            """);

        // License
        sb.Append("""
            <h1 id="license">License</h1>
            <p>Insight Training Studio is available in the following plans (B2B only):</p>

            <h2>Plan Comparison</h2>
            <table>
                <tr><th>Feature</th><th>FREE</th><th>TRIAL</th><th>BIZ</th><th>ENT</th></tr>
                <tr><td>Duration</td><td>Unlimited</td><td>30 days</td><td>365 days</td><td>Custom</td></tr>
                <tr><td>Video Generation</td><td>Yes</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>VOICEVOX Narration</td><td>Yes</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Text Overlays</td><td>Yes</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>BGM</td><td>Yes</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Video Export (MP4)</td><td>Yes</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Subtitles</td><td>No</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Subtitle Styles</td><td>No</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Transitions</td><td>No</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>PPTX Import</td><td>No</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Max Resolution</td><td>1080p</td><td>1080p</td><td>1080p</td><td>4K</td></tr>
                <tr><td>Max File Size</td><td>200MB</td><td>200MB</td><td>200MB</td><td>Unlimited</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">About Plans:</span>
                The FREE plan provides core features with no time limit.
                The TRIAL plan provides full access to all features for 30 days.
                Upgrade to BIZ for continued use.
            </div>

            <h2>License Activation</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">Go to Help > License Manager</div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">Enter the email and license key from your purchase</div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">Click "Activate"</div></div>

            <div class="note">
                <span class="note-label">Note:</span>
                License keys are specific to Insight Training Studio. Keys for other HARMONIC insight products cannot be used.
            </div>

            <hr class="section-divider">
            """);

        // System Requirements
        sb.Append("""
            <h1 id="system-req">System Requirements</h1>
            <table>
                <tr><th>Requirement</th><th>Details</th></tr>
                <tr><td>OS</td><td>Windows 10 / 11 (64-bit)</td></tr>
                <tr><td>Runtime</td><td>.NET 8.0 Desktop Runtime</td></tr>
                <tr><td>Required Software</td><td>VOICEVOX Engine (for narration)</td></tr>
                <tr><td>Required Tool</td><td>FFmpeg (for video generation)</td></tr>
                <tr><td>Memory</td><td>4GB+ recommended</td></tr>
                <tr><td>Storage</td><td>500MB+ free space (excluding video output)</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // AI Assistant
        sb.Append("""
            <h1 id="ai-assistant">AI Assistant</h1>
            <p>Use Claude AI to create narration, edit subtitles, analyze video structure, generate thumbnails,
            and more. Open the panel using the assistant button in the top-right corner.</p>

            <h2>Getting Started</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Set your API key</strong> — Click the 🔑 button in the panel header, enter your Anthropic API key, and click "Set".
                <br><em>Your key is encrypted with DPAPI and stored locally on this device only. It is never shared externally.</em>
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Choose a model</strong> — Select the Claude model from the dropdown at the top-left of the panel.
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Enter a prompt</strong> — Type your question or instruction and click "Execute" (or press <kbd>Ctrl</kbd>+<kbd>Enter</kbd>).
            </div></div>

            <h2>Panel Buttons</h2>
            <table>
                <tr><th>Button</th><th>Function</th></tr>
                <tr><td>?</td><td>Help — Opens this page at the AI Assistant section</td></tr>
                <tr><td>⧉</td><td>Pop Out — Opens the AI panel in a standalone window</td></tr>
                <tr><td>🔑</td><td>API Key — Set or change your Anthropic API key</td></tr>
            </table>

            <h2>Preset Prompts</h2>
            <p>25 ready-made prompt templates organized into 10 categories. Expand the 🎯 section to view them.
            Click any preset to load it into the editor.</p>
            <table>
                <tr><th>Category</th><th>Content</th></tr>
                <tr><td>Subtitles &amp; Translation</td><td>Generate subtitles from narration, JP/EN translation</td></tr>
                <tr><td>Narration</td><td>Generate narration from slide notes, proofreading</td></tr>
                <tr><td>Structure Advice</td><td>Check video structure, evaluate educational effectiveness</td></tr>
                <tr><td>Thumbnail</td><td>Auto-generate thumbnails, title &amp; catchphrase suggestions</td></tr>
                <tr><td>Training &amp; Education</td><td>Onboarding, procedure conversion, safety training</td></tr>
                <tr><td>Business</td><td>Product introduction, internal announcements, meeting summaries</td></tr>
                <tr><td>One-Click</td><td>Full auto video from slides, bilingual JP/EN</td></tr>
                <tr><td>Tone Adjustment</td><td>Make concise, formal, or casual</td></tr>
                <tr><td>Marketing</td><td>SNS short scripts, elevator pitches</td></tr>
                <tr><td>Quality</td><td>Normalize length, pronunciation hints, unify tone</td></tr>
            </table>
            <ul>
                <li>Hover over a preset to preview its full prompt text in a tooltip.</li>
                <li>Right-click &gt; "Save to My Prompts" to create a customizable copy.</li>
            </ul>

            <h2>Execution Modes</h2>
            <p>Presets have two execution modes:</p>
            <table>
                <tr><th>Mode</th><th>Behavior</th><th>Use Cases</th></tr>
                <tr><td><strong>check</strong> (Auto-execute)</td><td>AI uses tools to directly modify scene narration, subtitles, thumbnails, etc.</td><td>Subtitle generation, narration setup, tone adjustment</td></tr>
                <tr><td><strong>advice</strong> (Advisory)</td><td>AI returns analysis and suggestions as text. No data is modified.</td><td>Structure check, educational evaluation, title suggestions</td></tr>
            </table>

            <h2>AI Execute (Tool-Enabled Prompts)</h2>
            <p>The <strong>"AI Execute"</strong> button in the Production ribbon group lets you select and run tool-enabled AI prompts.
            This feature enables AI to automatically control scenes using 14 specialized tools.</p>

            <h3>How to Use</h3>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>AI Execute Button</strong> — Click "AI Execute" in the Production ribbon group
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Select Prompt</strong> — Choose the prompt you want to run from the dialog
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Select Prompt</strong> — Click "Select Prompt" to load it into the AI Assistant panel and start execution
            </div></div>

            <h3>Available Tools</h3>
            <p>AI can use the following tools to manipulate scenes:</p>
            <table>
                <tr><th>Tool</th><th>Function</th></tr>
                <tr><td>get_scenes</td><td>Get information about all scenes</td></tr>
                <tr><td>set_multiple_scenes</td><td>Update multiple scenes at once (narration, subtitles)</td></tr>
                <tr><td>add_scene</td><td>Add a new scene</td></tr>
                <tr><td>delete_scene</td><td>Delete a specific scene</td></tr>
                <tr><td>reorder_scenes</td><td>Change scene order</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">Auto-apply vs Advisory:</span>
                Each prompt is marked as either "Auto-apply" or "Advisory" mode.
                Auto-apply prompts directly edit scenes through AI tools.
                Advisory prompts present recommendations as text, letting you review before applying.
            </div>

            <h2>Thumbnail Auto-Generation</h2>
            <p>AI analyzes your video content and automatically generates optimal thumbnail images (1280×720px).</p>
            <ol>
                <li>Click the "🖼️ Auto-generate thumbnails" preset</li>
                <li>AI analyzes the video content and generates 3 thumbnail patterns</li>
                <li>A thumbnail preview appears in the result area</li>
                <li>Click "Save Thumbnail" to save as a PNG file</li>
            </ol>
            <p>AI selects from 11 layout patterns and 6 color styles to match your video content.
            If a scene has image media, it can be used as a background image.</p>

            <h2>My Prompts (Prompt Library)</h2>
            <p>Save prompts you use frequently for quick access.
            Build up a library of refined prompts to improve your workflow over time.
            Expand the 📚 section to view them.</p>

            <h3>How to Save Prompts</h3>
            <table>
                <tr><th>Method</th><th>Steps</th></tr>
                <tr><td>From input</td><td>Type a prompt, then click the 💾 button below the input area</td></tr>
                <tr><td>From presets</td><td>Right-click a preset &gt; "Save to My Prompts"</td></tr>
            </table>

            <h3>Managing Prompts</h3>
            <p>Right-click a My Prompt card to access the following actions:</p>
            <ul>
                <li><strong>Run</strong> — Click a card to load the prompt into the editor.</li>
                <li><strong>Edit</strong> — Right-click &gt; "Edit" to change label, category, icon, or prompt text.</li>
                <li><strong>Duplicate</strong> — Right-click &gt; "Duplicate" to create a new prompt based on an existing one.</li>
                <li><strong>Delete</strong> — Right-click &gt; "Delete" to permanently remove a prompt.</li>
            </ul>

            <h3>Data Storage</h3>
            <p>My Prompts are saved as individual JSON files in a local folder on your device.
            They are never sent to the cloud.</p>
            <pre>%LOCALAPPDATA%\InsightCast\Prompts\</pre>

            <h2>Usage Monitoring</h2>
            <p>Once an API key is set, usage statistics are displayed at the top of the panel:</p>
            <table>
                <tr><th>Item</th><th>Description</th></tr>
                <tr><td>Calls</td><td>Number of API calls made</td></tr>
                <tr><td>Tokens</td><td>Input / Output token counts</td></tr>
                <tr><td>Cost</td><td>Estimated API cost for the session (USD)</td></tr>
            </table>
            <div class="tip">
                <span class="tip-label">API Costs:</span>
                API usage is billed directly by Anthropic to your account (BYOK model).
                This product does not charge any API usage fees.
            </div>

            <hr class="section-divider">
            """);

        // Support
        sb.Append("""
            <h1 id="support">Support</h1>
            <table>
                <tr><th>Item</th><th>Details</th></tr>
                <tr><td>Email</td><td>support@h-insight.jp</td></tr>
                <tr><td>Developer</td><td>HARMONIC insight</td></tr>
                <tr><td>Website</td><td><a href="https://www.insight-office.com/ja">https://www.insight-office.com/ja</a></td></tr>
            </table>

            <h2>Legal</h2>
            <ul>
                <li><a href="https://www.insight-office.com/ja/ja/terms">Terms of Service</a></li>
                <li><a href="https://www.insight-office.com/ja/ja/privacy">Privacy Policy</a></li>
            </ul>

            <h2>About VOICEVOX</h2>
            <p>This product uses the VOICEVOX speech engine.
            Please review each character's usage terms when using VOICEVOX voices.</p>
            """);
    }
}
