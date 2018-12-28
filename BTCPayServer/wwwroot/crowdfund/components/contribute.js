Vue.component('contribute', {
    props: ["targetCurrency", "active", "inModal"],
    data: function () {
        return {
            email: "",
            amount: 0,
            choiceKey: ""
        }
    },
    methods: {
        onContributeFormSubmit: function (e) {
            if (e) {
                e.preventDefault();
            }
            if(!this.active){
                return;
            }
            eventAggregator.$emit("contribute", {email: this.email, amount: this.amount, choiceKey: this.choiceKey});
        }
    },

    template: "<div>" +
        "<h3 v-if='!inModal'>Contribute</h3>" +
        "" +
        "                            <form v-on:submit=\"onContributeFormSubmit\">" +
        "" +
        "                                <div class=\"form-group\">" +
        "                                    <label ></label>" +
        "                                    <input type=\"email\" class=\"form-control\" v-model=\"email\" placeholder='Email'/>" +
        "                                </div>" +
        "                                <div class=\"form-group\">" +
        "                                    <label ></label>" +
        "                                    <div class=\"input-group mb-3\">" +
        "                                        <input type=\"number\" step=\"any\" class=\"form-control\" v-model=\"amount\" placeholder='Contribution Amount'/>" +
        "                                        <div class=\"input-group-append\">" +
        "                                            <span class=\"input-group-text\">{{targetCurrency}}</span>" +
        "                                        </div>" +
        "                                    </div>" +
        "                                </div>" +
        "                                <button type=\"submit\" class=\"btn btn-primary\" :disabled='!active' v-if='!inModal'>Contribute</button>" +
        "                            </form>" +
        "</div>"
});
