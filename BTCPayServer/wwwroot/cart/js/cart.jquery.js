$(document).ready(function(){
    var cart = new Cart();

    // Destroy the cart when the "pay button is clicked"
    $('#js-cart-pay').click(function(){
        cart.destroy(true);
    });

    $('#js-cart-summary').find('tbody').prepend(cart.template($('#template-cart-tip'), {
        'tip': cart.fromCents(cart.getTip()) || ''
    }));

    $('#cartModal').one('show.bs.modal', function () {
        cart.updateDiscount();
        cart.updateTip();
        cart.updateSummaryProducts();
        cart.updateSummaryTotal();

        $('.js-cart-tip-btn').click(function(event){
            event.preventDefault();

            var $tip = $('.js-cart-tip'),
                discount = cart.percentage(cart.getTotalProducts(), cart.getDiscount());

            var purchaseAmount = cart.getTotalProducts() - discount;
            var tipPercentage = parseInt($(this).data('tip'));
            var tipValue = cart.percentage(purchaseAmount, tipPercentage).toFixed(srvModel.currencyInfo.divisibility);
            $tip.val(tipValue);
            $tip.trigger('input');
        });
    });
});
