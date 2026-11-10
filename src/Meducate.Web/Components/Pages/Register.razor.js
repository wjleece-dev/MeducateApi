export function initScrollCheck(modalBodyId, buttonId) {
    const body = document.getElementById(modalBodyId);
    const btn = document.getElementById(buttonId);
    if (!body || !btn) return;

    const check = () => {
        if (body.scrollTop + body.clientHeight >= body.scrollHeight - 10) {
            btn.disabled = false;
        }
    };

    // Enable immediately if content doesn't overflow
    requestAnimationFrame(check);
    body.addEventListener('scroll', check);
}
