const description = document.getElementById('description');
const products = document.getElementById('products');
const tips = document.getElementById('tips');
const cart = document.getElementById('cart-display');
const keypad = document.getElementById('keypad-display');
const discounts = document.getElementById('discounts');
const buttonPriceText = document.getElementById('button-price-text');
const customPayments = document.getElementById('custom-payments');

function hide(el) {
    el.setAttribute('hidden', true);
}
function show(el) {
    el.removeAttribute('hidden');
}
function updateFormForDefaultView(type) {
    switch (type) {
        case 'Static':
        case 'Print':
            hide(tips);
            hide(cart);
            hide(keypad);
            hide(discounts);
            hide(buttonPriceText);
            show(description);
            show(products);
            show(customPayments);
            break;
        case 'Cart':
            show(cart);
            show(tips);
            show(products);
            show(discounts);
            show(description);
            show(buttonPriceText);
            hide(customPayments);
            hide(keypad);
            break;
        case 'Light':
            show(tips);
            show(discounts);
            show(keypad);
            hide(cart);
            hide(description);
            hide(buttonPriceText);
            hide(customPayments);
            document.getElementById('ShowItems').checked ? show(products) : hide(products);
            break;
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const defaultView = document.querySelector('input[name="DefaultView"]:checked');
    if (defaultView) {
        updateFormForDefaultView(defaultView.value);
    }
});

delegate('change', 'input[name="DefaultView"]', e => {
    updateFormForDefaultView(e.target.value);
});

delegate('change', 'input[name="ShowItems"]', e => {
    e.target.checked ? show(products) : hide(products);
});
