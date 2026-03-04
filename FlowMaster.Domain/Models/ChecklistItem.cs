using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FlowMaster.Domain.Models
{
    /// <summary>
    /// 테스트 체크리스트 항목.
    /// IsHeader=true인 정수 RowNo 행은 서브항목 평가코드를 자동 집계합니다.
    /// </summary>
    public class ChecklistItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>항목 ID (자동증가)</summary>
        public int ItemId { get; set; }

        /// <summary>문서 ID (FK: ApprovalDocument)</summary>
        public int DocId { get; set; }

        /// <summary>항목 번호 ("1", "1.1", "2.3" 등)</summary>
        public string RowNo { get; set; }

        /// <summary>확인 항목명 (고정 텍스트)</summary>
        public string CheckItem { get; set; }

        /// <summary>산출물</summary>
        private string _outputContent;
        public string OutputContent
        {
            get => _outputContent;
            set
            {
                if (_outputContent != value)
                {
                    _outputContent = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>평가 코드 (+, (+), (-), -, nb). 헤더 행은 서브항목에서 자동 집계.</summary>
        private string _evaluationCode;
        public string EvaluationCode
        {
            get => _evaluationCode;
            set
            {
                if (_evaluationCode != value)
                {
                    _evaluationCode = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>비고</summary>
        private string _remarks;
        public string Remarks
        {
            get => _remarks;
            set
            {
                if (_remarks != value)
                {
                    _remarks = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>표시 순서</summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// 소수점 없는 RowNo ("1", "2", "3", "4")는 헤더(서브타이틀).
        /// EvaluationCode는 서브항목들의 집계값으로 자동 계산됨 (읽기 전용).
        /// </summary>
        public bool IsHeader => !string.IsNullOrEmpty(RowNo) && !RowNo.Contains(".");

        /// <summary>Clone을 위한 복사 생성자</summary>
        public ChecklistItem Clone()
        {
            return new ChecklistItem
            {
                RowNo = this.RowNo,
                CheckItem = this.CheckItem,
                OutputContent = this.OutputContent,
                EvaluationCode = this.EvaluationCode,
                Remarks = this.Remarks,
                DisplayOrder = this.DisplayOrder
                // DocId와 ItemId는 새로 할당
            };
        }
    }
}
