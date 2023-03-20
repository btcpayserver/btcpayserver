document.addEventListener("DOMContentLoaded", () => {
    delegate('click', 'button.toggle-password', e => {
        const button = e.target.closest('button.toggle-password');
        const input = button.previousSibling.previousSibling;
        if(input.type === 'password'){
            input.type = 'text';
            button.querySelector('.shown-as-password').style.display = 'none';
            button.querySelector('.shown-as-text').style.display = 'block';
        }else{
            input.type = 'password';
            button.querySelector('.shown-as-text').style.display = 'none';
            button.querySelector('.shown-as-password').style.display = 'block';
        }
    });
})
