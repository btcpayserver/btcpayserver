$.fn.addAnimate = function(completeCallback) {
    if ($(this).find('.js-cart-added').length === 0) {
        $(this).append('<div class="js-cart-added"><i class="fa fa-check fa-3x text-white align-middle"></i></div>');
        
        // Animate the element
        $(this).find('.js-cart-added').fadeIn(200, function(){
            var self = this;
            // Show it for 200ms
            setTimeout(function(){
                // Hide and remove
                $(self).fadeOut(100, function(){
                    $(this).remove();

                    completeCallback && completeCallback();
                })
            }, 200);
        });
    }
}

function removeAccents(input){
	var accents 	= 'ÀÁÂÃÄÅàáâãäåÒÓÔÕÕÖØòóôõöøÈÉÊËèéêëðÇČçčÐĎďÌÍÎÏìíîïĽľÙÚÛÜùúûüÑŇñňŠšŤťŸÿýŽž  ́',
		accentsOut 	= 'AAAAAAaaaaaaOOOOOOOooooooEEEEeeeeeCCccDDdIIIIiiiiLlUUUUuuuuNNnnSsTtYyyZz ',
		output 		= '',
		index 		= -1;
	
	for( var i = 0; i < input.length; i++ ) {
		index = accents.indexOf(input[i]);
		
		if( index != -1 ) {
			output += typeof accentsOut[index] != 'undefined' ? accentsOut[index] : '';
		}
		else {
			output += typeof input[i] != 'undefined' ? input[i] : '';
		}
	}
	
	return output;
}

jQuery.expr[':'].icontains = function (a, i, m) {
	var string = removeAccents(jQuery(a).text().toLowerCase());
	
	return string.indexOf(removeAccents(m[3].toLowerCase())) >= 0;
};

$(document).ready(function(){
    var cart = new Cart();

    $('.js-add-cart').click(function(event){
        event.preventDefault();

        var $btn = $(event.target),
            self = this;
            index = $btn.closest('.card').data('index'),
            item = srvModel.items[index],
            items = cart.items;

        // Is event catching disabled?
        if (!$(this).hasClass('disabled')) {
            // Disable catching events for this element
            $(this).addClass('disabled');

            // Add-to-cart animation only once
            $(this).addAnimate(function(){
                // Enable the event
                $(self).removeClass('disabled');
            });

            cart.addItem({
                id: item.id,
                title: item.title,
                price: item.price,
                image: typeof item.image != 'undefined' ? item.image : null,
                inventory: item.inventory
            });
            cart.listItems();
        }
    });

    // Destroy the cart when the "pay button is clicked"
    $('#js-cart-pay').click(function(){
        cart.destroy(true);
    });

    // Disable pay button and add loading animation when pay form is submitted
    $('#js-cart-pay-form').on('submit', function() {
        var button = $('#js-cart-pay');
        if (button) {
            // Disable the pay button
            button.attr('disabled', true);
            
            // Add loading animation to the pay button
            button.prepend([
                '<div class="spinner-grow spinner-grow-sm" role="status">',
                '    <span class="sr-only">Loading...</span>',
                '</div>'
            ].join(''));
        }
    });

    $('.js-cart').on('click', function () {
        $('#sidebar, #content').toggleClass('active');
        $('.collapse.in').toggleClass('in');
        $('a[aria-expanded=true]').attr('aria-expanded', 'false');
    });

    $('.js-search').keyup(function(event){
        var str = $(this).val();

        $('#js-pos-list').find(".card-wrapper").show();

        if (str.length > 1) {
            var $list = $('#js-pos-list').find(".card-title:not(:icontains('" + $.escapeSelector(str) + "'))");
            $list.parents('.card-wrapper').hide();
            $('.js-search-reset').show();
        } else if (str.length === 0) {
            $('.js-search-reset').hide();
        }
    });

    $('.js-search-reset').click(function(event){
        event.preventDefault();

        $('.js-search').val('');
        $('.js-search').trigger('keyup');
        $(this).hide();
    });

    $('#js-cart-summary').find('tbody').prepend(cart.template($('#template-cart-tip'), {
        'tip': cart.fromCents(cart.getTip()) || ''
    }));

    $('#cartModal').one('show.bs.modal', function () {
        cart.updateDiscount();
        cart.updateTip();
        cart.updateSummaryProducts();
        cart.updateSummaryTotal();

        // Change total when tip is changed
        $('.js-cart-tip').inputAmount(cart, 'tip');
        // Remove tip
        $('.js-cart-tip-remove').removeAmount(cart, 'tip');

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
