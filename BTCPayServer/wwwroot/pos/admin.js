const description = document.getElementById('description');
const products = document.getElementById('products');
const tips = document.getElementById('tips');
const cart = document.getElementById('cart-display');
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
            break;
        case 'Light':
            show(tips);
            show(discounts);
            hide(cart);
            hide(products);
            hide(description);
            hide(buttonPriceText);
            hide(customPayments);
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
