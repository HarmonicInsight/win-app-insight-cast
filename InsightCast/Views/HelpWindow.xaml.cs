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

        Title = _isEn ? "InsightCast - Help" : "InsightCast - ヘルプ";
        TocHeader.Text = _isEn ? "Contents" : "目次";
        SearchPlaceholder.Text = _isEn ? "Search..." : "検索...";

        _sectionIds = new[]
        {
            "overview", "ui-layout", "quick-mode", "scene-editor", "narration",
            "subtitle-text", "bgm-effects", "planning", "export",
            "shortcuts", "faq", "license", "system-req", "support"
        };

        _sectionNames = _isEn
            ? new[]
            {
                "Overview", "UI Layout", "Quick Mode", "Scene Editor", "Narration",
                "Subtitles & Text", "BGM & Effects", "Planning Tab", "Export",
                "Keyboard Shortcuts", "FAQ", "License", "System Requirements", "Support"
            }
            : new[]
            {
                "はじめに", "画面構成", "かんたんモード", "シーン編集", "ナレーション",
                "字幕・テキスト", "BGM・エフェクト", "企画・制作", "書き出し",
                "キーボードショートカット", "FAQ", "ライセンス", "システム要件", "お問い合わせ"
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
        if (e.Uri != null && e.Uri.Scheme.StartsWith("http", StringComparison.Ordinal))
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
                <h1>InsightCast</h1>
                <div class="subtitle">テキストを入力するだけで、ナレーション付き動画を自動生成</div>
                <div class="version">v{ver}</div>
            </div>

            <p>InsightCast は、VOICEVOX 音声エンジンを使用してナレーション付き動画を自動生成する Windows デスクトップアプリケーションです。
            PowerPoint、画像、動画ファイルを取り込み、テキストを入力するだけでプロ品質の動画を作成できます。</p>

            <div class="feature-grid">
                <div class="feature-card">
                    <h4>かんたん動画生成</h4>
                    <p>ファイルをドロップして設定するだけ。数分でナレーション付き動画が完成します。</p>
                </div>
                <div class="feature-card">
                    <h4>VOICEVOX ナレーション</h4>
                    <p>多彩なキャラクターボイスを使い分け。話速も自由に調整できます。</p>
                </div>
                <div class="feature-card">
                    <h4>字幕・テキスト装飾</h4>
                    <p>10種類以上のプリセットスタイル。テキストオーバーレイも自由に配置。</p>
                </div>
                <div class="feature-card">
                    <h4>サムネイル自動生成</h4>
                    <p>YouTube向けサムネイルをワンクリックで生成。カラーパターンも豊富。</p>
                </div>
            </div>

            <hr class="section-divider">
            """);

        // UI Layout
        sb.Append("""
            <h1 id="ui-layout">画面構成</h1>
            <p>InsightCast のメイン画面は、<strong>2つのタブ</strong>で構成されています。</p>

            <h2>タイトルバー</h2>
            <p>画面最上部に、ファイル操作ボタン（新規・開く・保存・別名保存・PPTX取込）と
            ライセンス管理ボタンが配置されています。</p>

            <h2>メニューバー</h2>
            <p>タイトルバーの下にメニューバーがあります。</p>
            <ul>
                <li><strong>ファイル</strong> — 新規作成、開く、最近使ったファイル、保存、別名保存、PPTX取込、JSONインポート/エクスポート、終了</li>
                <li><strong>編集</strong> — シーン追加/削除、シーンの並び替え</li>
                <li><strong>ヘルプ</strong> — ヘルプ、ライセンス管理、利用規約、プライバシーポリシー、バージョン情報</li>
            </ul>

            <h2>企画・制作タブ</h2>
            <p>動画の企画段階で使うタブです。3つの領域に分かれています。</p>
            <ul>
                <li><strong>クイックセットアップ（左）</strong> — 動画の長さ・シーン数の設定、素材のドロップ</li>
                <li><strong>構成・スクリプト（中央）</strong> — シーン構成の編集、JSON形式でのインポート</li>
                <li><strong>サムネイルジェネレーター（右）</strong> — YouTube向けサムネイルの作成</li>
            </ul>

            <h2>動画生成タブ</h2>
            <p>実際の動画編集・書き出しを行うタブです。</p>
            <ul>
                <li><strong>シーン一覧（左）</strong> — シーンの追加・削除・並び替え、解像度設定</li>
                <li><strong>シーン編集（中央）</strong> — 素材選択、ナレーション入力、字幕、テキストオーバーレイ、トランジション設定</li>
                <li><strong>書き出し設定（右）</strong> — 話者選択、BGM設定、動画書き出し</li>
            </ul>

            <h2>ステータスバー</h2>
            <p>画面下部に、VOICEVOX接続状態、FFmpeg検出状態、言語切替ボタンが表示されます。</p>

            <hr class="section-divider">
            """);

        // Quick Mode
        sb.Append("""
            <h1 id="quick-mode">かんたんモード</h1>
            <p>企画・制作タブの「クイックセットアップ」は、最速で動画を作成する方法です。</p>

            <h2>基本ワークフロー</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>素材を取り込む</strong> — ファイルをドロップゾーンにドラッグ＆ドロップ、または「ファイルを選択」ボタンをクリック。
                PowerPoint (.pptx)・画像 (PNG/JPG/BMP/GIF)・動画 (MP4/AVI/MOV)・テキスト (.txt) に対応。
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>設定を調整</strong> — 話者（ボイス）、向き（横動画/縦動画）、切り替え効果、話速を設定。
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>動画を生成</strong> — 「動画を生成」ボタンをクリック。保存先を選択すると自動で書き出しが始まります。
            </div></div>

            <div class="tip">
                <span class="tip-label">ヒント:</span> 複数ファイルを一度にドロップすると、ファイルごとにシーンが自動作成されます。
                「複数ファイルを一括取込」ボタンからも複数選択できます。
            </div>

            <h2>生成後の操作</h2>
            <ul>
                <li><strong>動画を開く</strong> — 完成した動画をデフォルトプレイヤーで再生</li>
                <li><strong>出力フォルダを開く</strong> — サムネイル (.jpg)、チャプター (.chapters.txt)、メタデータ (.metadata.txt) も同時に出力されます</li>
                <li><strong>設定変えて再生成</strong> — 話者や向きを変えて再生成</li>
                <li><strong>詳細エディタで開く</strong> — 動画生成タブに切り替えて細かい編集が可能</li>
            </ul>

            <h2>複数ファイルの一括取込</h2>
            <p>「複数ファイルを一括取込」ボタンをクリックすると、ファイルダイアログから複数ファイルをまとめて選択・取込できます。
            PPTX、画像、動画、テキストを混在させることも可能です。</p>

            <hr class="section-divider">
            """);

        // Scene Editor
        sb.Append("""
            <h1 id="scene-editor">シーン編集</h1>
            <p>動画生成タブでは、シーン単位で動画を構成・編集します。</p>

            <h2>シーンの追加・削除</h2>
            <ul>
                <li><strong>追加</strong> — 「＋追加」ボタン、または <kbd>Ctrl</kbd>+<kbd>T</kbd></li>
                <li><strong>削除</strong> — 「－削除」ボタン、または <kbd>Delete</kbd>（最低1シーン必要）</li>
                <li><strong>並び替え</strong> — 「↑」「↓」ボタン、または <kbd>Ctrl</kbd>+<kbd>↑</kbd> / <kbd>Ctrl</kbd>+<kbd>↓</kbd></li>
            </ul>

            <h2>素材の設定</h2>
            <p>各シーンに画像または動画素材を1つ設定できます。</p>
            <ul>
                <li>「選択」ボタンで画像・動画ファイルを選択</li>
                <li>プレビュー画像をクリックすると拡大表示</li>
                <li>「クリア」ボタンで素材を解除（黒背景になります）</li>
            </ul>

            <div class="tip">
                <span class="tip-label">ヒント:</span> 対応画像形式: PNG, JPG, BMP, GIF。対応動画形式: MP4, AVI, MOV, WMV, MKV, WebM。
            </div>

            <h2>動画素材の設定</h2>
            <p>動画ファイルを素材に設定した場合、「動画音声を残す」チェックボックスが表示されます。
            チェックを入れると、元の動画の音声がナレーションに加えて再生されます。</p>

            <h2>シーンの長さ</h2>
            <ul>
                <li><strong>自動</strong> — ナレーション音声の長さに応じて自動決定（推奨）</li>
                <li><strong>固定</strong> — 0.1〜60秒の範囲で固定値を指定</li>
            </ul>

            <h2>シーンプレビュー</h2>
            <p>「▶シーンプレビュー」ボタンで、現在のシーンを動画としてプレビューできます。
            ナレーション音声、字幕、テキストオーバーレイが反映された状態を確認できます。</p>

            <hr class="section-divider">
            """);

        // Narration
        sb.Append("""
            <h1 id="narration">ナレーション</h1>
            <p>InsightCast は VOICEVOX 音声エンジンを使用して、テキストから自然な日本語ナレーションを生成します。</p>

            <h2>ナレーションテキスト</h2>
            <p>シーン編集パネルのテキスト入力欄に、話させたい内容を入力します。
            入力したテキストが音声合成されてナレーションになります。</p>

            <h2>話者（ボイス）の選択</h2>
            <p>VOICEVOX エンジンに登録されている話者から選択できます。各話者にはスタイル（ノーマル、あまあま、ツンツンなど）があり、
            場面に応じて使い分けが可能です。</p>
            <ul>
                <li><strong>シーン個別の話者</strong> — 各シーンの「話者」ドロップダウンで設定（「デフォルト」を選ぶとプロジェクト設定を使用）</li>
                <li><strong>書き出し設定の話者</strong> — 右パネル書き出し設定の「話者」はプロジェクト全体のデフォルト</li>
            </ul>

            <h2>話速の調整</h2>
            <p>ナレーションの速さを4段階から選べます。</p>
            <table>
                <tr><th>設定</th><th>速度倍率</th><th>用途</th></tr>
                <tr><td>ゆっくり</td><td>0.8x</td><td>ゆっくり丁寧に説明したい場合</td></tr>
                <tr><td>標準</td><td>1.0x</td><td>通常の速さ（デフォルト）</td></tr>
                <tr><td>やや速い</td><td>1.2x</td><td>テンポよく進めたい場合</td></tr>
                <tr><td>速い</td><td>1.5x</td><td>短時間で多くの情報を伝えたい場合</td></tr>
            </table>

            <h2>試聴</h2>
            <p>「▶試聴」ボタンで、入力したナレーションテキストの音声を事前に確認できます。
            ナレーションテキストが空の場合は試聴できません。</p>

            <div class="note">
                <span class="note-label">VOICEVOX エンジンについて:</span>
                InsightCast を使用するには、VOICEVOX エンジンが起動している必要があります。
                初回起動時のセットアップウィザードで接続設定を行います。
                接続が切れた場合は、VOICEVOX を再起動してからアプリを再起動してください。
            </div>

            <hr class="section-divider">
            """);

        // Subtitles & Text
        sb.Append("""
            <h1 id="subtitle-text">字幕・テキスト</h1>

            <h2>字幕</h2>
            <p>各シーンに字幕テキストを設定できます。字幕は動画の下部に表示されます。</p>
            <ul>
                <li>「字幕」欄にテキストを入力</li>
                <li>空欄の場合、字幕は表示されません</li>
            </ul>

            <h3>字幕スタイル</h3>
            <p>「選択...」ボタンで字幕スタイル設定ダイアログが開きます。10種類以上のプリセットスタイルから選べます。</p>
            <table>
                <tr><th>スタイル名</th><th>特徴</th></tr>
                <tr><td>デフォルト</td><td>白文字に黒縁取り。シンプルで読みやすい</td></tr>
                <tr><td>ニュース風</td><td>ニュース番組のテロップ風</td></tr>
                <tr><td>映画風</td><td>映画字幕のエレガントなスタイル</td></tr>
                <tr><td>バラエティ風</td><td>明るくポップなスタイル</td></tr>
                <tr><td>ドキュメンタリー風</td><td>落ち着いた情報番組風</td></tr>
                <tr><td>教育・解説風</td><td>わかりやすい教育コンテンツ向け</td></tr>
                <tr><td>ホラー風</td><td>ダークで不気味なスタイル</td></tr>
                <tr><td>かわいい風</td><td>パステルカラーのかわいいスタイル</td></tr>
                <tr><td>テック風</td><td>モダンなテクノロジー系スタイル</td></tr>
                <tr><td>エレガント風</td><td>高級感のあるスタイル</td></tr>
            </table>

            <h3>カスタム設定</h3>
            <p>プリセットをベースに、以下を個別にカスタマイズできます。</p>
            <ul>
                <li><strong>フォント</strong> — 游ゴシック UI、メイリオ、MS ゴシック、BIZ UDゴシック など</li>
                <li><strong>サイズ</strong> — フォントサイズ</li>
                <li><strong>文字色</strong> — 任意のカラーを指定</li>
                <li><strong>縁取り色</strong> — 文字の縁取りの色</li>
                <li><strong>縁取り幅</strong> — 縁取りの太さ</li>
            </ul>

            <h2>テキストオーバーレイ</h2>
            <p>字幕とは別に、画面上の任意の位置にテキストを配置できます。タイトル、キャプション、注釈などに使えます。</p>

            <h3>オーバーレイの追加</h3>
            <ul>
                <li>「＋」ボタンで新しいオーバーレイを追加</li>
                <li>「タイトル追加」ボタンで表紙用のタイトル＋サブタイトルを一括追加</li>
            </ul>

            <h3>オーバーレイの設定項目</h3>
            <table>
                <tr><th>項目</th><th>説明</th></tr>
                <tr><td>テキスト</td><td>表示する文字列</td></tr>
                <tr><td>横位置 (%)</td><td>画面左端からの水平位置（0〜100%）</td></tr>
                <tr><td>縦位置 (%)</td><td>画面上端からの垂直位置（0〜100%）</td></tr>
                <tr><td>文字サイズ</td><td>フォントサイズ</td></tr>
                <tr><td>揃え</td><td>左・中央・右</td></tr>
                <tr><td>文字色</td><td>テキストの色</td></tr>
                <tr><td>不透明度</td><td>0%（完全透明）〜100%（不透明）</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">ヒント:</span> オーバーレイの表示順は「↑」「↓」ボタンで変更できます。上にあるほど前面に表示されます。
            </div>

            <hr class="section-divider">
            """);

        // BGM & Effects
        sb.Append("""
            <h1 id="bgm-effects">BGM・エフェクト</h1>

            <h2>BGM（バックグラウンドミュージック）</h2>
            <p>書き出し設定パネルの「BGM設定...」ボタンから、BGMの詳細設定ができます。</p>

            <h3>BGMファイル</h3>
            <p>対応形式: MP3, WAV, OGG, M4A, AAC, FLAC, WMA</p>

            <h3>音量設定</h3>
            <table>
                <tr><th>設定</th><th>説明</th></tr>
                <tr><td>メイン音量</td><td>BGMの基本音量</td></tr>
                <tr><td>ダッキング</td><td>ナレーション再生中にBGM音量を自動で下げる機能</td></tr>
                <tr><td>ダッキング音量</td><td>ダッキング時の音量</td></tr>
                <tr><td>アタック</td><td>ダッキングが始まるまでの時間</td></tr>
                <tr><td>リリース</td><td>ダッキングから通常音量に戻るまでの時間</td></tr>
            </table>

            <h3>フェード設定</h3>
            <ul>
                <li><strong>フェードイン</strong> — BGMの始まりを徐々に音量を上げて自然に開始</li>
                <li><strong>フェードアウト</strong> — BGMの終わりを徐々に音量を下げて自然に終了</li>
                <li><strong>ループ再生</strong> — 動画がBGMより長い場合、BGMを繰り返し再生</li>
            </ul>

            <h2>トランジション</h2>
            <p>シーン間の切り替え効果を設定できます。</p>
            <table>
                <tr><th>エフェクト</th><th>説明</th></tr>
                <tr><td>なし</td><td>瞬時に切り替え</td></tr>
                <tr><td>フェード</td><td>フェードイン・フェードアウト</td></tr>
                <tr><td>ディゾルブ</td><td>前後のシーンが重なって切り替わる</td></tr>
                <tr><td>ワイプ（左/右）</td><td>画面を拭き取るように切り替え</td></tr>
                <tr><td>スライド（左/右）</td><td>画面がスライドして切り替え</td></tr>
                <tr><td>ズームイン</td><td>ズームしながら切り替え</td></tr>
            </table>
            <p>トランジションの時間は 0.2〜2.0 秒の範囲で調整できます。</p>

            <h2>ロゴ透かし（ウォーターマーク）</h2>
            <p>動画にロゴやブランド画像を重ねて表示できます。</p>
            <ul>
                <li>「選択」ボタンでロゴ画像（PNG推奨）を選択</li>
                <li>表示位置: 左上、右上、左下、右下、中央 から選択</li>
                <li>「クリア」ボタンでロゴを解除</li>
            </ul>

            <hr class="section-divider">
            """);

        // Planning Tab
        sb.Append("""
            <h1 id="planning">企画・制作タブ</h1>
            <p>企画・制作タブは、動画の構成を事前に計画し、効率的に制作を進めるための機能です。</p>

            <h2>クイックセットアップ</h2>
            <p>左パネルで動画の基本設定と素材の取り込みを行います。</p>
            <ul>
                <li><strong>動画の目的</strong> — 製品紹介、チュートリアル、概念解説、プロモーション から選択</li>
                <li><strong>長さ</strong> — 15秒〜180秒（3分）の範囲で設定</li>
                <li><strong>シーン数</strong> — 動画を構成するシーンの数を設定</li>
                <li><strong>構成に反映</strong> — 設定をシーンリストに反映</li>
            </ul>

            <h2>構成・スクリプト</h2>
            <p>中央パネルでシーンごとのタイトルとスクリプト（ナレーション原稿）を編集します。</p>

            <h3>シーンリストモード</h3>
            <p>各シーンのタイトルとスクリプトを直接編集できます。</p>

            <h3>JSONモード</h3>
            <p>外部AIで生成したJSON形式のシーン構成をペーストして一括適用できます。「JSONを適用」ボタンで反映されます。</p>

            <h2>画像プロンプトビルダー</h2>
            <p>AI画像生成用のプロンプトを組み立てるツールです。</p>
            <ul>
                <li><strong>シーンタイプ</strong> — 製品紹介、概念解説、チュートリアル手順、機能ショーケースなど</li>
                <li><strong>構図</strong> — ワイドショット、クローズアップ、中央配置、三分割法、アイソメトリック</li>
                <li><strong>雰囲気</strong> — プロフェッショナル、フレンドリー、モダン、エレガント、ドラマチック</li>
                <li><strong>ビジュアルスタイル</strong> — 写実的、イラスト、3D、フラット、ミニマル</li>
            </ul>
            <p>生成されたプロンプトは DALL-E、Midjourney 等の画像生成サービスで使用できます。</p>

            <h2>サムネイルジェネレーター</h2>
            <p>右パネルでYouTube向けサムネイル（1280×720px）を作成できます。</p>
            <ul>
                <li><strong>パターン選択</strong> — カラーパターンを選択</li>
                <li><strong>ワンクリックスタイル</strong> — プリセットスタイルを適用</li>
                <li><strong>メインテキスト</strong> — 大きく表示されるメインタイトル（10文字以内推奨）</li>
                <li><strong>サブテキスト</strong> — 補足テキスト</li>
                <li><strong>背景</strong> — 画像または単色の背景</li>
                <li><strong>テキスト色</strong> — カラーパレットから選択</li>
            </ul>

            <h2>動画生成タブへの引き継ぎ</h2>
            <p>「動画生成へ」ボタンで、企画・制作タブで作成した構成を動画生成タブに引き継ぎ、
            詳細な編集・書き出しに進めます。</p>

            <hr class="section-divider">
            """);

        // Export
        sb.Append("""
            <h1 id="export">書き出し</h1>

            <h2>動画の書き出し</h2>
            <p>動画生成タブの右パネルで書き出し設定を行い、「動画を書き出し」ボタンで動画を生成します。</p>

            <h3>書き出し設定</h3>
            <table>
                <tr><th>設定</th><th>説明</th></tr>
                <tr><td>話者</td><td>プロジェクト全体のデフォルト話者</td></tr>
                <tr><td>解像度</td><td>1920×1080（横動画）または 1080×1920（縦動画）</td></tr>
                <tr><td>BGM設定</td><td>バックグラウンドミュージックの設定</td></tr>
            </table>

            <h3>出力ファイル</h3>
            <p>動画の書き出し時に、以下のファイルが同フォルダに自動生成されます。</p>
            <table>
                <tr><th>ファイル</th><th>説明</th></tr>
                <tr><td>○○○.mp4</td><td>動画ファイル</td></tr>
                <tr><td>○○○_thumbnail.jpg</td><td>サムネイル画像（1280×720px）</td></tr>
                <tr><td>○○○.chapters.txt</td><td>チャプターファイル（YouTubeの説明欄に貼り付け可能）</td></tr>
                <tr><td>○○○.metadata.txt</td><td>メタデータ（タイトル案・説明文・タグ候補）</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">ヒント:</span> チャプターファイルをYouTubeの説明欄にそのまま貼り付けると、動画にチャプターマーカーが自動設定されます。
            </div>

            <h2>プロジェクトの保存</h2>
            <p>InsightCast のプロジェクトは <code>.icproj</code> 形式で保存されます。
            <kbd>Ctrl</kbd>+<kbd>S</kbd> で上書き保存、<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> で別名保存。</p>

            <h2>PPTX取込</h2>
            <p>PowerPoint (.pptx) ファイルを取り込むと、スライドごとにシーンが自動作成されます。
            スライドの画像とスピーカーノートが自動的にシーンの素材・ナレーションに設定されます。</p>

            <div class="note">
                <span class="note-label">注意:</span> PPTX取込機能は Trial 以上のプランで利用可能です。
            </div>

            <h2>JSONインポート/エクスポート</h2>
            <ul>
                <li><strong>JSONインポート</strong> — ファイル → JSONインポート で、JSON形式のプロジェクトを読み込み</li>
                <li><strong>JSONエクスポート</strong> — ファイル → JSONエクスポート で、複数プロジェクトを一括書き出し</li>
            </ul>

            <h2>テンプレート</h2>
            <p>BGM・透かし・解像度などの設定をテンプレートとして保存・読込できます。
            「テンプレ保存」で現在の設定を保存、「テンプレ読込」で保存済み設定を適用します。</p>

            <hr class="section-divider">
            """);

        // Keyboard Shortcuts
        sb.Append("""
            <h1 id="shortcuts">キーボードショートカット</h1>
            <table>
                <tr><th>ショートカット</th><th>機能</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>N</kbd></td><td>新規プロジェクト</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>プロジェクトを開く</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>上書き保存</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>名前を付けて保存</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>T</kbd></td><td>シーンを追加</td></tr>
                <tr><td><kbd>Delete</kbd></td><td>シーンを削除</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>↑</kbd></td><td>シーンを上へ移動</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>↓</kbd></td><td>シーンを下へ移動</td></tr>
                <tr><td><kbd>F1</kbd></td><td>ヘルプを表示</td></tr>
                <tr><td><kbd>Alt</kbd>+<kbd>F4</kbd></td><td>アプリケーションを終了</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // FAQ
        sb.Append("""
            <h1 id="faq">よくある質問（FAQ）</h1>

            <h2>セットアップ・接続</h2>

            <h3>Q: VOICEVOX が接続できません。</h3>
            <p>A: VOICEVOX エンジンが起動しているか確認してください。
            通常、VOICEVOX はローカルの <code>http://127.0.0.1:50021</code> で動作しています。
            初回起動時のセットアップウィザードで接続設定を行います。
            接続が切れた場合は、VOICEVOX を再起動してから InsightCast を再起動してください。</p>

            <h3>Q: FFmpeg が見つからないと表示されます。</h3>
            <p>A: FFmpeg は動画生成に必須のツールです。以下の方法で導入してください。</p>
            <ul>
                <li>PATH環境変数に ffmpeg.exe のあるフォルダを追加</li>
                <li>アプリフォルダ内に <code>tools\ffmpeg\bin\ffmpeg.exe</code> を配置</li>
                <li><code>build.ps1</code> を実行して自動ダウンロード</li>
            </ul>

            <h2>動画生成</h2>

            <h3>Q: 動画の書き出しに失敗します。</h3>
            <p>A: 以下を確認してください。</p>
            <ul>
                <li>FFmpeg がインストールされ、PATH に含まれているか</li>
                <li>VOICEVOX エンジンが起動しているか</li>
                <li>保存先のディスクに十分な空き容量があるか</li>
                <li>保存先のパスに日本語や特殊文字が含まれていないか</li>
            </ul>

            <h3>Q: 書き出しをキャンセルしたい。</h3>
            <p>A: 進捗バー横の「キャンセル」ボタンをクリックしてください。</p>

            <h3>Q: 素材なしでも動画は作れますか？</h3>
            <p>A: はい。素材が設定されていないシーンは黒背景で生成されます。
            テキストオーバーレイと字幕を組み合わせれば、画像なしでも動画を作成できます。</p>

            <h2>機能・対応形式</h2>

            <h3>Q: 対応ファイル形式は？</h3>
            <p>A:</p>
            <table>
                <tr><th>種類</th><th>対応形式</th></tr>
                <tr><td>画像</td><td>PNG, JPG/JPEG, BMP, GIF</td></tr>
                <tr><td>動画</td><td>MP4, AVI, MOV, WMV, MKV, WebM</td></tr>
                <tr><td>音声 (BGM)</td><td>MP3, WAV, OGG, FLAC, AAC, M4A, WMA</td></tr>
                <tr><td>その他</td><td>PPTX, TXT, JSON</td></tr>
            </table>

            <h3>Q: 字幕やトランジションが使えません。</h3>
            <p>A: Trial 以上のプランが必要です。「ヘルプ」→「ライセンス管理」からライセンスをアクティベートしてください。</p>

            <h3>Q: テンプレートはどこに保存されますか？</h3>
            <p>A: <code>%LOCALAPPDATA%\InsightCast\Templates</code> に保存されます。</p>

            <h2>その他</h2>

            <h3>Q: 言語を切り替えたい。</h3>
            <p>A: ステータスバーの言語切替ボタン（JA / EN）をクリックすると、日本語と英語を切り替えられます。</p>

            <h3>Q: ライセンスキーの入力方法は？</h3>
            <p>A: メニュー「ヘルプ」→「ライセンス管理」からメールアドレスとライセンスキーを入力し、
            「アクティベート」ボタンをクリックしてください。</p>

            <hr class="section-divider">
            """);

        // License
        sb.Append("""
            <h1 id="license">ライセンス</h1>
            <p>InsightCast は以下の3つのプランで提供されています。用途に合わせてお選びください。</p>

            <h2>プラン比較表</h2>
            <table>
                <tr><th>機能</th><th>Free</th><th>Trial</th><th>Pro</th></tr>
                <tr><td>シーン作成</td><td>3シーンまで</td><td>無制限</td><td>無制限</td></tr>
                <tr><td>VOICEVOX ナレーション</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>字幕表示</td><td>—</td><td>○</td><td>○</td></tr>
                <tr><td>字幕スタイル（10種以上）</td><td>—</td><td>○</td><td>○</td></tr>
                <tr><td>トランジション効果</td><td>—</td><td>○</td><td>○</td></tr>
                <tr><td>PPTX取込</td><td>—</td><td>○</td><td>○</td></tr>
                <tr><td>テンプレート保存・読込</td><td>—</td><td>○</td><td>○</td></tr>
                <tr><td>テキストオーバーレイ</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>サムネイル生成</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>BGM設定</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>動画書き出し（MP4）</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>チャプター・メタデータ出力</td><td>○</td><td>○</td><td>○</td></tr>
                <tr><td>バッチエクスポート</td><td>—</td><td>—</td><td>○</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">おすすめ:</span>
                まずは Free プランで基本機能をお試しください。
                字幕やトランジションが必要になったら Trial / Pro へアップグレードできます。
            </div>

            <h2>こんな方におすすめ</h2>
            <table>
                <tr><th>プラン</th><th>おすすめの方</th></tr>
                <tr><td><strong>Free</strong></td><td>まずは試してみたい方、シンプルな動画を作りたい方</td></tr>
                <tr><td><strong>Trial</strong></td><td>字幕付き動画を作りたい方、PPTX変換を使いたい方</td></tr>
                <tr><td><strong>Pro</strong></td><td>業務で大量の動画を制作する方、全機能を使いたい方</td></tr>
            </table>

            <h2>ライセンスの管理</h2>

            <h3>ライセンスの確認</h3>
            <p>メニュー → ヘルプ → ライセンス情報 で、現在のプラン・状態・有効期限を確認できます。</p>

            <h3>ライセンスの入力・アクティベート</h3>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                メニュー → ヘルプ → 「ライセンス管理」をクリック
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                購入時に届いたメールアドレスとライセンスキーを入力
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                「アクティベート」ボタンをクリック
            </div></div>

            <div class="note">
                <span class="note-label">ご注意:</span>
                ライセンスキーは InsightCast 専用です。他の HARMONIC insight 製品のキーは使用できません。
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
                <tr><td>必須ソフトウェア</td><td>VOICEVOX エンジン</td></tr>
                <tr><td>必須ツール</td><td>FFmpeg（動画生成に必要）</td></tr>
                <tr><td>メモリ</td><td>4GB以上推奨</td></tr>
                <tr><td>ストレージ</td><td>500MB以上の空き容量（動画出力を含まず）</td></tr>
            </table>

            <h2>必須コンポーネントのインストール</h2>

            <h3>VOICEVOX エンジン</h3>
            <p>VOICEVOX 公式サイトからダウンロード・インストールしてください。
            InsightCast 起動前に VOICEVOX エンジンを起動しておく必要があります。</p>

            <h3>FFmpeg</h3>
            <p>動画の生成・プレビューに FFmpeg が必要です。
            <code>build.ps1</code> を実行すると自動ダウンロードされます。
            手動で導入する場合は、ffmpeg.exe を PATH に追加するか、
            アプリフォルダ内の <code>tools\ffmpeg\bin\</code> に配置してください。</p>

            <hr class="section-divider">
            """);

        // Support
        sb.Append("""
            <h1 id="support">お問い合わせ</h1>
            <p>ご不明な点やお困りのことがございましたら、以下よりお問い合わせください。</p>

            <h2>サポート窓口</h2>
            <table>
                <tr><th>項目</th><th>情報</th></tr>
                <tr><td>メール</td><td>info@h-insight.jp</td></tr>
                <tr><td>開発元</td><td>HARMONIC insight 合同会社</td></tr>
                <tr><td>Web</td><td><a href="https://www.insight-office.com">https://www.insight-office.com</a></td></tr>
            </table>

            <h2>利用規約・プライバシーポリシー</h2>
            <ul>
                <li><a href="https://www.insight-office.com/ja/terms">利用規約</a></li>
                <li><a href="https://www.insight-office.com/ja/privacy">プライバシーポリシー</a></li>
            </ul>

            <h2>VOICEVOX について</h2>
            <p>InsightCast は VOICEVOX 音声エンジンを利用しています。
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
                <h1>InsightCast</h1>
                <div class="subtitle">Auto-generate narrated videos from text input</div>
                <div class="version">v{ver}</div>
            </div>

            <p>InsightCast is a Windows desktop application that automatically generates narrated videos using the VOICEVOX text-to-speech engine.
            Import PowerPoint presentations, images, or video files, enter your narration text, and create professional-quality videos in minutes.</p>

            <div class="feature-grid">
                <div class="feature-card">
                    <h4>Easy Video Generation</h4>
                    <p>Drop files and configure settings. Narrated videos are ready in minutes.</p>
                </div>
                <div class="feature-card">
                    <h4>VOICEVOX Narration</h4>
                    <p>Choose from a variety of character voices. Adjust speech speed freely.</p>
                </div>
                <div class="feature-card">
                    <h4>Subtitles & Text</h4>
                    <p>10+ preset subtitle styles. Place text overlays anywhere on screen.</p>
                </div>
                <div class="feature-card">
                    <h4>Auto Thumbnail</h4>
                    <p>Generate YouTube thumbnails with one click. Rich color patterns available.</p>
                </div>
            </div>

            <hr class="section-divider">
            """);

        // UI Layout
        sb.Append("""
            <h1 id="ui-layout">UI Layout</h1>
            <p>InsightCast's main window is organized into <strong>two tabs</strong>.</p>

            <h2>Title Bar</h2>
            <p>The top bar contains file operation buttons (New, Open, Save, Save As, Import PPTX) and a License Manager button.</p>

            <h2>Menu Bar</h2>
            <p>Below the title bar is the menu bar:</p>
            <ul>
                <li><strong>File</strong> — New, Open, Recent Files, Save, Save As, Import PPTX, JSON Import/Export, Exit</li>
                <li><strong>Edit</strong> — Add/Remove Scene, Reorder Scenes</li>
                <li><strong>Help</strong> — Help, License Manager, Terms, Privacy Policy, About</li>
            </ul>

            <h2>Planning Tab</h2>
            <p>Used during the video planning stage. Divided into three areas:</p>
            <ul>
                <li><strong>Quick Setup (Left)</strong> — Set video duration, scene count, drop media files</li>
                <li><strong>Structure & Script (Center)</strong> — Edit scene structure, JSON import</li>
                <li><strong>Thumbnail Generator (Right)</strong> — Create YouTube thumbnails</li>
            </ul>

            <h2>Video Generation Tab</h2>
            <p>Where you edit and export your video:</p>
            <ul>
                <li><strong>Scene List (Left)</strong> — Add, remove, reorder scenes; set resolution</li>
                <li><strong>Scene Editor (Center)</strong> — Media selection, narration input, subtitles, overlays, transitions</li>
                <li><strong>Export Settings (Right)</strong> — Speaker selection, BGM settings, video export</li>
            </ul>

            <h2>Status Bar</h2>
            <p>Shows VOICEVOX connection status, FFmpeg detection status, and language toggle (JA/EN).</p>

            <hr class="section-divider">
            """);

        // Quick Mode
        sb.Append("""
            <h1 id="quick-mode">Quick Mode</h1>
            <p>The Quick Setup panel in the Planning tab is the fastest way to create a video.</p>

            <h2>Basic Workflow</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">
                <strong>Import media</strong> — Drag & drop files onto the drop zone, or click "Select File".
                Supports PowerPoint (.pptx), images (PNG/JPG/BMP/GIF), video (MP4/AVI/MOV), and text (.txt).
            </div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">
                <strong>Configure settings</strong> — Set speaker (voice), orientation (landscape/portrait), transition, and speed.
            </div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">
                <strong>Generate video</strong> — Click "Generate Video", choose save location, and export starts automatically.
            </div></div>

            <div class="tip">
                <span class="tip-label">Tip:</span> Dropping multiple files at once creates one scene per file.
                You can also use the "Import multiple files" button for batch selection.
            </div>

            <h2>After Generation</h2>
            <ul>
                <li><strong>Open Video</strong> — Play the completed video in your default player</li>
                <li><strong>Open Output Folder</strong> — View thumbnail (.jpg), chapters (.chapters.txt), and metadata (.metadata.txt)</li>
                <li><strong>Regenerate</strong> — Change speaker or orientation and generate again</li>
                <li><strong>Open in Editor</strong> — Switch to Video Generation tab for detailed editing</li>
            </ul>

            <hr class="section-divider">
            """);

        // Scene Editor
        sb.Append("""
            <h1 id="scene-editor">Scene Editor</h1>
            <p>In the Video Generation tab, you compose and edit your video scene by scene.</p>

            <h2>Adding & Removing Scenes</h2>
            <ul>
                <li><strong>Add</strong> — "+ Add" button or <kbd>Ctrl</kbd>+<kbd>T</kbd></li>
                <li><strong>Remove</strong> — "- Remove" button or <kbd>Delete</kbd> (minimum 1 scene required)</li>
                <li><strong>Reorder</strong> — Arrow buttons or <kbd>Ctrl</kbd>+<kbd>Up</kbd> / <kbd>Ctrl</kbd>+<kbd>Down</kbd></li>
            </ul>

            <h2>Setting Media</h2>
            <p>Each scene can have one image or video file as its media:</p>
            <ul>
                <li>Click "Select" to choose an image/video file</li>
                <li>Click the preview image to enlarge</li>
                <li>Click "Clear" to remove media (black background)</li>
            </ul>

            <div class="tip">
                <span class="tip-label">Tip:</span> Supported images: PNG, JPG, BMP, GIF. Supported videos: MP4, AVI, MOV, WMV, MKV, WebM.
            </div>

            <h2>Video Media Options</h2>
            <p>When a video file is set as media, the "Keep original audio" checkbox appears.
            When checked, the video's original audio plays alongside narration.</p>

            <h2>Scene Duration</h2>
            <ul>
                <li><strong>Auto</strong> — Duration matches narration length (recommended)</li>
                <li><strong>Fixed</strong> — Set a specific duration from 0.1 to 60 seconds</li>
            </ul>

            <h2>Scene Preview</h2>
            <p>Click "Preview Scene" to generate a video preview of the current scene with narration, subtitles, and overlays applied.</p>

            <hr class="section-divider">
            """);

        // Narration
        sb.Append("""
            <h1 id="narration">Narration</h1>
            <p>InsightCast uses the VOICEVOX speech engine to generate natural Japanese narration from text.</p>

            <h2>Narration Text</h2>
            <p>Enter the text you want spoken in the narration input area of the scene editor.
            The text will be synthesized into speech and used as narration.</p>

            <h2>Speaker Selection</h2>
            <p>Choose from speakers registered in the VOICEVOX engine. Each speaker has styles (Normal, Sweet, Tsundere, etc.):</p>
            <ul>
                <li><strong>Per-scene speaker</strong> — Set in each scene's "Speaker" dropdown ("Default" uses project settings)</li>
                <li><strong>Export speaker</strong> — The "Speaker" in export settings is the project-wide default</li>
            </ul>

            <h2>Speech Speed</h2>
            <table>
                <tr><th>Setting</th><th>Speed</th><th>Best For</th></tr>
                <tr><td>Slow</td><td>0.8x</td><td>Careful, detailed explanations</td></tr>
                <tr><td>Normal</td><td>1.0x</td><td>Standard pace (default)</td></tr>
                <tr><td>Slightly Fast</td><td>1.2x</td><td>Brisk, efficient delivery</td></tr>
                <tr><td>Fast</td><td>1.5x</td><td>Information-dense content</td></tr>
            </table>

            <h2>Audio Preview</h2>
            <p>Click "Play Audio" to preview the narration before generating. Narration text must not be empty.</p>

            <div class="note">
                <span class="note-label">About VOICEVOX:</span>
                The VOICEVOX engine must be running for InsightCast to work.
                The setup wizard configures the connection on first launch.
                If disconnected, restart VOICEVOX and then restart the app.
            </div>

            <hr class="section-divider">
            """);

        // Subtitles & Text
        sb.Append("""
            <h1 id="subtitle-text">Subtitles & Text</h1>

            <h2>Subtitles</h2>
            <p>Set subtitle text for each scene. Subtitles appear at the bottom of the video.</p>
            <ul>
                <li>Enter text in the "Subtitle" field</li>
                <li>Leave empty for no subtitles</li>
            </ul>

            <h3>Subtitle Styles</h3>
            <p>Click "Select..." to open the style dialog. Choose from 10+ preset styles:</p>
            <table>
                <tr><th>Style</th><th>Description</th></tr>
                <tr><td>Default</td><td>White text with black outline. Simple and readable.</td></tr>
                <tr><td>News</td><td>News broadcast ticker style</td></tr>
                <tr><td>Cinema</td><td>Elegant movie subtitle style</td></tr>
                <tr><td>Variety</td><td>Bright and pop style</td></tr>
                <tr><td>Documentary</td><td>Calm information program style</td></tr>
                <tr><td>Education</td><td>Clear educational content style</td></tr>
                <tr><td>Horror</td><td>Dark and eerie style</td></tr>
                <tr><td>Cute</td><td>Pastel-colored cute style</td></tr>
                <tr><td>Tech</td><td>Modern technology style</td></tr>
                <tr><td>Elegant</td><td>Premium, sophisticated style</td></tr>
            </table>

            <h3>Custom Settings</h3>
            <p>Customize font, size, text color, stroke color, and stroke width based on presets.</p>

            <h2>Text Overlays</h2>
            <p>Place text anywhere on screen, separate from subtitles. Useful for titles, captions, and annotations.</p>

            <h3>Overlay Properties</h3>
            <table>
                <tr><th>Property</th><th>Description</th></tr>
                <tr><td>Text</td><td>The text to display</td></tr>
                <tr><td>Horizontal (%)</td><td>Position from left (0-100%)</td></tr>
                <tr><td>Vertical (%)</td><td>Position from top (0-100%)</td></tr>
                <tr><td>Font Size</td><td>Text size</td></tr>
                <tr><td>Alignment</td><td>Left, Center, or Right</td></tr>
                <tr><td>Color</td><td>Text color</td></tr>
                <tr><td>Opacity</td><td>0% (transparent) to 100% (opaque)</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // BGM & Effects
        sb.Append("""
            <h1 id="bgm-effects">BGM & Effects</h1>

            <h2>BGM (Background Music)</h2>
            <p>Click "BGM Settings..." in the export panel to configure background music.</p>

            <h3>Supported Formats</h3>
            <p>MP3, WAV, OGG, M4A, AAC, FLAC, WMA</p>

            <h3>Volume Settings</h3>
            <table>
                <tr><th>Setting</th><th>Description</th></tr>
                <tr><td>Main Volume</td><td>Base BGM volume</td></tr>
                <tr><td>Ducking</td><td>Auto-lower BGM during narration</td></tr>
                <tr><td>Attack</td><td>Time to start ducking</td></tr>
                <tr><td>Release</td><td>Time to return to normal volume</td></tr>
            </table>

            <h3>Fade & Loop</h3>
            <ul>
                <li><strong>Fade In</strong> — Gradually increase volume at the start</li>
                <li><strong>Fade Out</strong> — Gradually decrease volume at the end</li>
                <li><strong>Loop</strong> — Repeat BGM if video is longer than the music</li>
            </ul>

            <h2>Transitions</h2>
            <table>
                <tr><th>Effect</th><th>Description</th></tr>
                <tr><td>None</td><td>Instant switch</td></tr>
                <tr><td>Fade</td><td>Fade in/out</td></tr>
                <tr><td>Dissolve</td><td>Cross-dissolve between scenes</td></tr>
                <tr><td>Wipe (L/R)</td><td>Wipe transition left or right</td></tr>
                <tr><td>Slide (L/R)</td><td>Slide transition left or right</td></tr>
                <tr><td>Zoom In</td><td>Zoom transition</td></tr>
            </table>
            <p>Transition duration: 0.2 to 2.0 seconds.</p>

            <h2>Logo Watermark</h2>
            <p>Overlay a logo or brand image on your video. Position: Top-Left, Top-Right, Bottom-Left, Bottom-Right, or Center.</p>

            <hr class="section-divider">
            """);

        // Planning Tab
        sb.Append("""
            <h1 id="planning">Planning Tab</h1>
            <p>Plan your video structure before editing for efficient production.</p>

            <h2>Quick Setup</h2>
            <ul>
                <li><strong>Video Purpose</strong> — Product intro, tutorial, concept explanation, or promotion</li>
                <li><strong>Duration</strong> — 15 to 180 seconds (3 minutes)</li>
                <li><strong>Scene Count</strong> — Number of scenes in the video</li>
            </ul>

            <h2>Structure & Script</h2>
            <p>Edit scene titles and narration scripts in the center panel. Use JSON mode for importing AI-generated structures.</p>

            <h2>Image Prompt Builder</h2>
            <p>Build AI image generation prompts with scene type, composition, mood, and visual style selectors.
            Copy the prompt and use it in DALL-E, Midjourney, or similar services.</p>

            <h2>Thumbnail Generator</h2>
            <p>Create YouTube thumbnails (1280x720px) with color patterns, text styling, and background images.</p>

            <h2>Transfer to Video</h2>
            <p>Click "Transfer to Video" to carry your planned structure into the Video Generation tab for detailed editing.</p>

            <hr class="section-divider">
            """);

        // Export
        sb.Append("""
            <h1 id="export">Export</h1>

            <h2>Video Export</h2>
            <p>Configure export settings in the right panel and click "Export Video".</p>

            <h3>Export Settings</h3>
            <table>
                <tr><th>Setting</th><th>Description</th></tr>
                <tr><td>Speaker</td><td>Default speaker for the project</td></tr>
                <tr><td>Resolution</td><td>1920x1080 (Landscape) or 1080x1920 (Portrait)</td></tr>
                <tr><td>BGM</td><td>Background music settings</td></tr>
            </table>

            <h3>Output Files</h3>
            <table>
                <tr><th>File</th><th>Description</th></tr>
                <tr><td>*.mp4</td><td>Video file</td></tr>
                <tr><td>*_thumbnail.jpg</td><td>Thumbnail image (1280x720)</td></tr>
                <tr><td>*.chapters.txt</td><td>Chapter file (paste in YouTube description)</td></tr>
                <tr><td>*.metadata.txt</td><td>Metadata (title, description, tags)</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">Tip:</span> Paste the chapters file content into your YouTube description to auto-create chapter markers.
            </div>

            <h2>Project Files</h2>
            <p>Projects are saved as <code>.icproj</code> files.
            <kbd>Ctrl</kbd>+<kbd>S</kbd> to save, <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> to save as.</p>

            <h2>PPTX Import</h2>
            <p>Import PowerPoint (.pptx) files to auto-create scenes from slides. Slide images and speaker notes become scene media and narration.</p>

            <div class="note">
                <span class="note-label">Note:</span> PPTX import requires a Trial plan or above.
            </div>

            <h2>Templates</h2>
            <p>Save and load settings templates (BGM, watermark, resolution) with "Save Template" and "Load Template".</p>

            <hr class="section-divider">
            """);

        // Keyboard Shortcuts
        sb.Append("""
            <h1 id="shortcuts">Keyboard Shortcuts</h1>
            <table>
                <tr><th>Shortcut</th><th>Action</th></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>N</kbd></td><td>New Project</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>Open Project</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>Save</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>Save As</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>T</kbd></td><td>Add Scene</td></tr>
                <tr><td><kbd>Delete</kbd></td><td>Remove Scene</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Up</kbd></td><td>Move Scene Up</td></tr>
                <tr><td><kbd>Ctrl</kbd>+<kbd>Down</kbd></td><td>Move Scene Down</td></tr>
                <tr><td><kbd>F1</kbd></td><td>Show Help</td></tr>
                <tr><td><kbd>Alt</kbd>+<kbd>F4</kbd></td><td>Exit Application</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // FAQ
        sb.Append("""
            <h1 id="faq">FAQ</h1>

            <h2>Setup & Connection</h2>

            <h3>Q: Cannot connect to VOICEVOX.</h3>
            <p>A: Ensure the VOICEVOX engine is running. It typically runs at <code>http://127.0.0.1:50021</code>.
            The setup wizard configures the connection on first launch.
            If disconnected, restart VOICEVOX and then restart InsightCast.</p>

            <h3>Q: FFmpeg not found.</h3>
            <p>A: FFmpeg is required for video generation. Install it by:</p>
            <ul>
                <li>Adding the ffmpeg.exe folder to your PATH</li>
                <li>Placing <code>tools\ffmpeg\bin\ffmpeg.exe</code> in the app folder</li>
                <li>Running <code>build.ps1</code> for automatic download</li>
            </ul>

            <h2>Video Generation</h2>

            <h3>Q: Video export fails.</h3>
            <p>A: Check that FFmpeg is installed, VOICEVOX is running, there's enough disk space,
            and the save path doesn't contain special characters.</p>

            <h3>Q: How to cancel an export?</h3>
            <p>A: Click the "Cancel" button next to the progress bar.</p>

            <h2>Features</h2>

            <h3>Q: Supported file formats?</h3>
            <table>
                <tr><th>Type</th><th>Formats</th></tr>
                <tr><td>Images</td><td>PNG, JPG/JPEG, BMP, GIF</td></tr>
                <tr><td>Videos</td><td>MP4, AVI, MOV, WMV, MKV, WebM</td></tr>
                <tr><td>Audio (BGM)</td><td>MP3, WAV, OGG, FLAC, AAC, M4A, WMA</td></tr>
                <tr><td>Other</td><td>PPTX, TXT, JSON</td></tr>
            </table>

            <h3>Q: Subtitles and transitions are not available.</h3>
            <p>A: These features require a Trial plan or above. Go to Help > License Manager to activate your license.</p>

            <h3>Q: How to switch language?</h3>
            <p>A: Click the language toggle button (JA/EN) in the status bar.</p>

            <h3>Q: How to enter a license key?</h3>
            <p>A: Go to Help > License Manager, enter your email and license key, then click "Activate".</p>

            <hr class="section-divider">
            """);

        // License
        sb.Append("""
            <h1 id="license">License</h1>
            <p>InsightCast is available in three plans. Choose the one that fits your needs.</p>

            <h2>Plan Comparison</h2>
            <table>
                <tr><th>Feature</th><th>Free</th><th>Trial</th><th>Pro</th></tr>
                <tr><td>Scene Creation</td><td>Up to 3</td><td>Unlimited</td><td>Unlimited</td></tr>
                <tr><td>VOICEVOX Narration</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Subtitles</td><td>—</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Subtitle Styles (10+)</td><td>—</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Transitions</td><td>—</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>PPTX Import</td><td>—</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Templates</td><td>—</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Text Overlays</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Thumbnail Generator</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>BGM Settings</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Video Export (MP4)</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Chapter & Metadata</td><td>Yes</td><td>Yes</td><td>Yes</td></tr>
                <tr><td>Batch Export</td><td>—</td><td>—</td><td>Yes</td></tr>
            </table>

            <div class="tip">
                <span class="tip-label">Tip:</span>
                Start with the Free plan to explore the basics.
                Upgrade to Trial or Pro when you need subtitles, transitions, or PPTX import.
            </div>

            <h2>License Activation</h2>
            <div class="step"><div class="step-num">1</div><div class="step-text">Go to Help > License Manager</div></div>
            <div class="step"><div class="step-num">2</div><div class="step-text">Enter the email and license key from your purchase</div></div>
            <div class="step"><div class="step-num">3</div><div class="step-text">Click "Activate"</div></div>

            <div class="note">
                <span class="note-label">Note:</span>
                License keys are specific to InsightCast. Keys for other HARMONIC insight products cannot be used.
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
                <tr><td>Required Software</td><td>VOICEVOX Engine</td></tr>
                <tr><td>Required Tool</td><td>FFmpeg (for video generation)</td></tr>
                <tr><td>Memory</td><td>4GB+ recommended</td></tr>
                <tr><td>Storage</td><td>500MB+ free space (excluding video output)</td></tr>
            </table>

            <hr class="section-divider">
            """);

        // Support
        sb.Append("""
            <h1 id="support">Support</h1>
            <table>
                <tr><th>Item</th><th>Details</th></tr>
                <tr><td>Email</td><td>info@h-insight.jp</td></tr>
                <tr><td>Developer</td><td>HARMONIC insight LLC</td></tr>
                <tr><td>Website</td><td><a href="https://www.insight-office.com">https://www.insight-office.com</a></td></tr>
            </table>

            <h2>Legal</h2>
            <ul>
                <li><a href="https://www.insight-office.com/ja/terms">Terms of Service</a></li>
                <li><a href="https://www.insight-office.com/ja/privacy">Privacy Policy</a></li>
            </ul>

            <h2>About VOICEVOX</h2>
            <p>InsightCast uses the VOICEVOX speech engine.
            Please review each character's usage terms when using VOICEVOX voices.</p>
            """);
    }
}
