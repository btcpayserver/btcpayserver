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
            id = $btn.closest('.card').data('id'),
            item = srvModel.items[id],
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
                id: id,
                title: item.title,
                price: item.price,
                image: typeof item.image != 'underfined' ? item.image : null
            });
            cart.listItems();
        }
    });

    // Destroy the cart when the "pay button is clicked"
    $('#js-cart-pay').click(function(){
        cart.destroy(true);
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
            var $list = $('#js-pos-list').find(".card-title:not(:icontains('" + str + "'))");
            $list.parents('.card-wrapper').hide();
            $('.js-search-reset').show();
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

            $tip.val(cart.percentage(cart.getTotalProducts() - discount, parseInt($(this).data('tip'))));
            $tip.trigger('input');
        });
    });
});