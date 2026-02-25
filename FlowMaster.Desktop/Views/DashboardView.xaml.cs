using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace FlowMaster.Desktop.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        private void AllDocsGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // 기본 정렬 동작 취소 (클라이언트 측 페이지네이션과 충돌 방지)
            e.Handled = true;

            var vm = DataContext as ViewModels.DashboardViewModel;
            if (vm == null) return;

            var column = e.Column;
            var sortPath = column.SortMemberPath;

            // ViewModel의 현재 정렬 상태 기준으로 토글
            // (column.SortDirection은 WPF 내부 동작으로 신뢰할 수 없음)
            ListSortDirection newDir;
            if (vm.CurrentSortColumn == sortPath)
                newDir = vm.CurrentSortAscending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else
                newDir = ListSortDirection.Ascending;

            // 모든 컬럼의 정렬 표시 초기화
            foreach (var col in AllDocsGrid.Columns)
                col.SortDirection = null;

            // 선택된 컬럼에 화살표 표시
            column.SortDirection = newDir;

            vm.SortByColumn(sortPath, newDir == ListSortDirection.Ascending);
        }

        private void SearchReset_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.DashboardViewModel;
            if (vm == null) return;
            vm.SearchText = "";
            vm.SearchCommand.Execute(null);
        }
    }
}
