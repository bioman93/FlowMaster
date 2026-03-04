using System;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using FlowMaster.Desktop.ViewModels;
using FlowMaster.Domain.Models;

namespace FlowMaster.Desktop.Views
{
    public partial class TestInputView : UserControl
    {
        private TestInputViewModel _currentVm;
        private bool _browserReady;

        public TestInputView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 이전 VM 이벤트 해제
            if (_currentVm != null)
            {
                _currentVm.AfterVersionSelected = null;
                _currentVm.GetDescriptionHtml = null;
                _currentVm.PropertyChanged -= Vm_PropertyChanged;
                _currentVm = null;
            }

            if (e.NewValue is TestInputViewModel vm)
            {
                // 버전 선택 후 TextBox 포커스 복원
                vm.AfterVersionSelected = () =>
                {
                    VersionBox.Focus();
                    VersionBox.CaretIndex = VersionBox.Text?.Length ?? 0;
                };

                // WebBrowser ↔ VM 연결
                _currentVm = vm;
                vm.GetDescriptionHtml = GetBrowserHtml;
                vm.PropertyChanged += Vm_PropertyChanged;

                _browserReady = false;
                LoadDescriptionHtml(vm.Description, vm.CanEdit);
            }
        }

        private void Vm_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is TestInputViewModel vm)
            {
                if (e.PropertyName == nameof(TestInputViewModel.Description))
                {
                    // 문서 로드/전환 시 브라우저 내용 갱신
                    _browserReady = false;
                    LoadDescriptionHtml(vm.Description, vm.CanEdit);
                }
                else if (e.PropertyName == nameof(TestInputViewModel.CanEdit))
                {
                    // 읽기 전용 전환 시(결재 상신 후 등) 편집 가능 여부 반영
                    SetBrowserEditable(vm.CanEdit);
                }
            }
        }

        /// <summary>
        /// Description 내용을 WebBrowser에 HTML로 로드.
        /// HTML 형식이면 그대로 렌더링, 일반 텍스트(레거시 데이터)면 &lt;p&gt;로 변환합니다.
        /// </summary>
        private void LoadDescriptionHtml(string description, bool canEdit)
        {
            string body;
            if (string.IsNullOrEmpty(description))
            {
                body = "";
            }
            else if (description.TrimStart().StartsWith("<"))
            {
                body = description;  // 이미 HTML
            }
            else
            {
                // 레거시 일반 텍스트 → HTML 인코딩 후 줄바꿈 보존
                body = "<p>" + WebUtility.HtmlEncode(description)
                    .Replace("\r\n", "<br>").Replace("\n", "<br>") + "</p>";
            }

            string editAttr = canEdit ? " contenteditable=\"true\"" : "";
            string cursorCss = canEdit ? "cursor:text;" : "";

            string html = $@"<html>
<head>
  <meta charset='utf-8'>
  <meta http-equiv='X-UA-Compatible' content='IE=edge'>
  <style>
    body {{ font-family:'Malgun Gothic',sans-serif; font-size:12px;
           margin:6px; padding:0; outline:none; {cursorCss}
           min-height:40px; word-wrap:break-word; }}
    table {{ border-collapse:collapse; margin-bottom:6px; }}
    td, th {{ border:1px solid #999; padding:4px 8px; }}
    th {{ background:#f2f2f2; }}
    p {{ margin:2px 0; }}
  </style>
</head>
<body{editAttr}>{body}</body>
</html>";

            DescriptionBrowser.NavigateToString(html);
        }

        private void DescriptionBrowser_LoadCompleted(object sender, NavigationEventArgs e)
        {
            _browserReady = true;
            if (_currentVm != null)
                SetBrowserEditable(_currentVm.CanEdit);
        }

        private void SetBrowserEditable(bool canEdit)
        {
            if (!_browserReady) return;
            try
            {
                dynamic doc = DescriptionBrowser.Document;
                if (doc?.body == null) return;
                doc.body.contentEditable = canEdit ? "true" : "false";
            }
            catch { /* IE DOM 접근 실패 시 무시 */ }
        }

        /// <summary>
        /// WebBrowser body.innerHTML 반환 — 저장 직전 ViewModel이 호출.
        /// </summary>
        private string GetBrowserHtml()
        {
            try
            {
                dynamic doc = DescriptionBrowser.Document;
                return doc?.body?.innerHTML as string ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 헤더 행(IsHeader=true)은 편집 불가.
        /// </summary>
        private void Checklist_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is ChecklistItem item && item.IsHeader)
                e.Cancel = true;
        }

        /// <summary>
        /// 셀이 포커스를 받으면 즉시 편집 진입.
        /// - 이전 행 파랗게 남는 문제 해결
        /// - TextBox 셀 두 번 클릭 문제 해결
        /// </summary>
        private void ChecklistCell_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (cell.DataContext is ChecklistItem item && item.IsHeader) return;
                var row = DataGridRow.GetRowContainingElement(cell);
                if (row != null &&
                    ItemsControl.ItemsControlFromItemContainer(row) is DataGrid dg)
                    dg.BeginEdit();
            }
        }

        /// <summary>
        /// 왼쪽 패널 산출물 버튼 클릭 처리.
        /// </summary>
        private void DocumentOutputButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && DataContext is TestInputViewModel vm)
            {
                if (!vm.CanEdit) return;
                vm.SelectDocumentOutput(btn.Content as string);
            }
        }

        /// <summary>
        /// 편집 진입 후 컨트롤별 UX 개선:
        /// - ComboBox(평가): 드롭다운 즉시 열기 + 값 선택 시 자동 커밋
        /// - TextBox(비고): 전체 텍스트 선택
        /// </summary>
        private void Checklist_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.EditingElement is ComboBox cb)
            {
                cb.DropDownClosed += (s, args) =>
                {
                    if (sender is DataGrid dg)
                        dg.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
                };
                Dispatcher.BeginInvoke(
                    new System.Action(() => cb.IsDropDownOpen = true),
                    DispatcherPriority.Input);
            }
            else if (e.EditingElement is TextBox tb)
            {
                Dispatcher.BeginInvoke(
                    new System.Action(() => { tb.Focus(); tb.SelectAll(); }),
                    DispatcherPriority.Input);
            }
        }
    }

    /// <summary>
    /// DataGrid 산출물 버튼의 선택 상태에 따라 배경색/전경색을 반환하는 컨버터.
    /// values[0]: 행의 OutputContent, values[1]: 버튼의 Content(이름)
    /// parameter="fg" → 전경색, 그 외 → 배경색
    /// </summary>
    public class OutputButtonBrushConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush SelectedBg
            = new SolidColorBrush(Color.FromRgb(0, 44, 95));     // #002c5f
        private static readonly SolidColorBrush SelectedFg = Brushes.White;
        private static readonly SolidColorBrush NormalBg   = Brushes.White;
        private static readonly SolidColorBrush NormalFg
            = new SolidColorBrush(Color.FromRgb(0, 44, 95));     // #002c5f

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var outputContent = values[0] as string;
            var buttonName    = values[1] as string;
            bool selected = string.Equals(outputContent, buttonName, StringComparison.OrdinalIgnoreCase);
            bool isFg     = string.Equals(parameter as string, "fg", StringComparison.OrdinalIgnoreCase);

            if (selected) return isFg ? (Brush)SelectedFg : SelectedBg;
            return isFg ? (Brush)NormalFg : NormalBg;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
