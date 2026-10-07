// 하단 탭바의 "지출 추가"를 누르면, 이미 지출 화면이면 입력 칸으로 바로 이동하고
// 다른 화면이면 /?add=1 로 이동합니다(이동 뒤에는 화면이 금액 칸에 포커스를 줍니다).
document.addEventListener('click', function (event) {
    var link = event.target.closest && event.target.closest('[data-quick-add]');
    if (!link) return;
    var amount = document.getElementById('expense-amount');
    if (!amount) return;
    event.preventDefault();
    amount.scrollIntoView({ block: 'center', behavior: 'smooth' });
    amount.focus({ preventScroll: true });
});
