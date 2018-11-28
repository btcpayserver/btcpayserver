$.fn.addAnimate = function(completeCallback) {
    var documentHeight = $(document).height(),
        itemPos = $(this).offset(),
        itemY = itemPos.top,
        cartPos = $('#js-cart').find('.badge').position();
        tempItem = '<span id="js-cart-temp-item" class="badge badge-primary text-white badge-pill " style="' +
                    'position: absolute;' +
                    'top: ' + itemPos.top + 'px;' +
                    'left: ' + (itemPos.left + 50) + 'px;">'+
                    '<i class="fa fa-shopping-basket"></i></span>';

    // Make animation speed look constant regardless of how far the object is from the cart
    var animationSpeed = (Math.log(itemY) * (documentHeight / Math.log2(documentHeight - itemY))) / 2;

    // Add the cart item badge and animate it
    $('body').after(tempItem);
    $('#js-cart-temp-item').animate({
        easing: 'swing',
        top: cartPos.top,
        left: cartPos.left
    }, animationSpeed, function() {
        $(this).remove();
        completeCallback && completeCallback();
    });
}; 

$(document).ready(function(){
    var cart = new Cart();

    $('.js-add-cart').click(function(event){
        event.preventDefault();

        var $btn = $(event.target),
            id = $btn.closest('.card').data('id'),
            item = srvModel.items[id];

        // Animate adding and then add then save
        $(this).addAnimate(function(){
            cart.addItem({
                id: id,
                title: item.title,
                price: item.price,
                image: typeof item.image != 'underfined' ? item.image : null
            });
        });
    });

    // Destroy the cart when the "pay button is clicked"
    $('#js-cart-pay').click(function(){
        cart.destroy();
    });

    // Repopulate cart items in the modal when it opens
    $('#cartModal').on('show.bs.modal', function () {
        cart.listItems();
    });
});