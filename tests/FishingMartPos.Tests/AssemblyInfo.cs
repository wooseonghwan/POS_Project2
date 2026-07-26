using Xunit;

// 이 테스트 프로젝트의 Repository 통합 테스트들은 실제 dev MySQL 인스턴스에 대해
// sale_no 자동 증가값과 sales_header_tb/sales_detail_tb 전역 상태를 공유한다.
// xUnit은 기본적으로 서로 다른 테스트 클래스(컬렉션)를 병렬로 실행하는데,
// 이 경우 한 테스트의 "안전망" 정리 로직(delete WHERE sale_no > baseline)이
// 동시에 실행 중인 다른 테스트가 방금 커밋한 행을 지워버리는 경쟁 상태가 발생한다.
// 공유 외부 리소스(DB)를 사용하는 통합 테스트이므로 컬렉션 간 병렬 실행을 끈다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
