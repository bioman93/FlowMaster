namespace FlowMaster.Domain.Models
{
    /// <summary>
    /// 테스트 체크리스트 항목
    /// </summary>
    public class ChecklistItem
    {
        /// <summary>항목 ID (자동증가)</summary>
        public int ItemId { get; set; }

        /// <summary>문서 ID (FK: ApprovalDocument)</summary>
        public int DocId { get; set; }

        /// <summary>항목 번호 ("1.1", "2.3" 등)</summary>
        public string RowNo { get; set; }

        /// <summary>확인 항목명 (고정 텍스트)</summary>
        public string CheckItem { get; set; }

        /// <summary>산출물</summary>
        public string OutputContent { get; set; }

        /// <summary>평가 코드 (+, (+), (-), -, nb)</summary>
        public string EvaluationCode { get; set; }

        /// <summary>비고</summary>
        public string Remarks { get; set; }

        /// <summary>표시 순서</summary>
        public int DisplayOrder { get; set; }

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
