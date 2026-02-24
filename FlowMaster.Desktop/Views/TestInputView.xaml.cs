using System.Windows.Controls;
using FlowMaster.Domain.Models;

namespace FlowMaster.Desktop.Views
{
    public partial class TestInputView : UserControl
    {
        public TestInputView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 헤더 행(IsHeader=true)은 편집 불가.
        /// 사용자가 헤더 셀을 클릭해도 편집 모드로 진입하지 않습니다.
        /// </summary>
        private void Checklist_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is ChecklistItem item && item.IsHeader)
                e.Cancel = true;
        }
    }
}
