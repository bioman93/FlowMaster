using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlowMaster.Desktop.ViewModels;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Desktop.Views
{
    public partial class AdminView : UserControl
    {
        public AdminView()
        {
            InitializeComponent();

            // DataContext 설정 후 Popup 바인딩 초기화
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is AdminViewModel vm)
                    InitAdSearchPopup(vm);
            };

            // 다른 곳 클릭 시 드롭다운 닫기
            LostFocus += (s, e) =>
            {
                if (DataContext is AdminViewModel vm && !adSearchPopup.IsKeyboardFocusWithin)
                    vm.IsAdSearchDropdownVisible = false;
            };
        }

        private void InitAdSearchPopup(AdminViewModel vm)
        {
            // Popup은 visual tree 외부라 DataContext 상속 안 됨 → 직접 설정
            adSearchList.DataContext = vm;
            adSearchPopup.PlacementTarget = adAccountTextBox;

            // ViewModel의 IsAdSearchDropdownVisible 변화를 Popup.IsOpen에 반영
            vm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(AdminViewModel.IsAdSearchDropdownVisible))
                    adSearchPopup.IsOpen = vm.IsAdSearchDropdownVisible;
            };
        }

        /// <summary>검색 결과 목록에서 항목 클릭 시 ViewModel에 전달합니다.</summary>
        private void AdSearchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem is User user)
            {
                if (DataContext is AdminViewModel vm)
                    vm.SelectAdSearchResult(user);

                lb.SelectedItem = null; // 선택 상태 초기화 (재선택 가능하도록)
            }
        }

        /// <summary>현재 멤버 목록 다중 선택 → ViewModel의 SelectedMembersToRemove 갱신.</summary>
        private void GroupMembersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && DataContext is AdminViewModel vm)
                vm.SelectedMembersToRemove = lb.SelectedItems.Cast<User>().ToList();
        }

        /// <summary>추가 가능 사용자 목록 다중 선택 → ViewModel의 SelectedMembersToAdd 갱신.</summary>
        private void NonGroupMembersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && DataContext is AdminViewModel vm)
                vm.SelectedMembersToAdd = lb.SelectedItems.Cast<User>().ToList();
        }

        /// <summary>체크리스트 항목 DataGrid 다중 선택 → ViewModel의 SelectedTemplateItems 갱신.</summary>
        private void TemplateItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && DataContext is AdminViewModel vm)
            {
                var items = new List<FmChecklistTemplateItemDto>();
                foreach (var item in grid.SelectedItems)
                {
                    if (item is FmChecklistTemplateItemDto dto)
                        items.Add(dto);
                }
                vm.SelectedTemplateItems = items;
            }
        }
    }
}
