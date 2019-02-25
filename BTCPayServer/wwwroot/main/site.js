$(function () {
    $(".localizeDate").each(function (index) {
        var serverDate = $(this).text();
        var localDate = new Date(serverDate);

        var dateString = localDate.toLocaleDateString() + " " + localDate.toLocaleTimeString();
        $(this).text(dateString);
    });


    $(".input-group-clear").on("click", function(){
        $(this).parents(".input-group").find("input").val(null);
    });

    $(".only-for-js").show();
});
