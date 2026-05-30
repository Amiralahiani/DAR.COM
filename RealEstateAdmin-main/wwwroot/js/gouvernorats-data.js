/**
 * Mapping gouvernorats → délégations pour la Tunisie (24 gouvernorats).
 * Structure prête à être complétée / corrigée avec les données officielles.
 */
const GOUVERNORATS_DATA = {
  "Ariana": [
    "Ariana Ville", "Ettadhamen", "Kalaat el-Andalous",
    "La Soukra", "Mnihla", "Raoued", "Sidi Thabet"
  ],
  "Béja": [
    "Amdoun", "Béja Nord", "Béja Sud", "Goubellat",
    "Medjez el-Bab", "Nefza", "Téboursouk", "Testour", "Thibar"
  ],
  "Ben Arous": [
    "Ben Arous", "Bou Mhel el-Bassatine", "El Mourouj", "Ezzahra",
    "Fouchana", "Hammam Chott", "Hammam Lif", "Khalidia",
    "La Nouvelle Ariana", "Medina Jedida", "Mégrine", "Mohamedia", "Radès"
  ],
  "Bizerte": [
    "Bizerte Nord", "Bizerte Sud", "El Alia", "Ghezala",
    "Ghar El Melh", "Joumine", "Mateur", "Menzel Bourguiba",
    "Menzel Jemil", "Ras Jebel", "Sejnane", "Tinja", "Utique"
  ],
  "Gabès": [
    "El Hamma", "Gabès Médina", "Gabès Ouest", "Gabès Sud",
    "Ghannouch", "Mareth", "Matmata", "Menzel el-Habib",
    "Métouia", "Nouvelle Matmata", "Toujane"
  ],
  "Gafsa": [
    "Belkhir", "El Guettar", "El Ksar", "Gafsa Nord", "Gafsa Sud",
    "Mdhilla", "Métlaoui", "Moularès", "Oum el-Araies",
    "Redeyef", "Sened", "Sidi Aïch"
  ],
  "Jendouba": [
    "Aïn Draham", "Balta-Bou Aouane", "Bou Salem", "Fernana",
    "Ghardimaou", "Jendouba", "Jendouba Nord", "Oued Mliz", "Tabarka"
  ],
  "Kairouan": [
    "Bou Hajla", "Chebika", "Cherarda", "El Ala", "Haffouz",
    "Hajeb el-Aïoun", "Kairouan Nord", "Kairouan Sud",
    "Nasrallah", "Oueslatia", "Sbikha"
  ],
  "Kasserine": [
    "Ayoun", "Ezzouhour", "Feriana", "Foussana", "Haidra",
    "Hassi el-Ferid", "Jediliane", "Kasserine Nord", "Kasserine Sud",
    "Majel Bel Abbès", "Sbeitla", "Sbiba", "Thélepte"
  ],
  "Kébili": [
    "Douz Nord", "Douz Sud", "El Faouar",
    "Kébili Nord", "Kébili Sud", "Souk Lahad"
  ],
  "Kef": [
    "Dahmani", "El Ksour", "Jerissa", "Kalaat Khasba", "Kalaat Sinane",
    "Kef Est", "Kef Ouest", "Nebeur", "Sakiet Sidi Youssef", "Tajerouine"
  ],
  "Mahdia": [
    "Bou Merdes", "Chébba", "Chorbane", "El Bradaa", "El Djem",
    "Essouassi", "Hebira", "Ksour Essef", "Mahdia",
    "Melloulèche", "Ouled Chamakh", "Sidi Alouane"
  ],
  "Manouba": [
    "Borj el-Amri", "Den Den", "Douar Hicher", "El Battan",
    "Jedaida", "La Manouba", "Mornaguia", "Oued Ellil", "Tébourba"
  ],
  "Médenine": [
    "Ben Gardane", "Beni Khedache", "Djerba Ajim",
    "Djerba Houmt Souk", "Djerba Midoun",
    "Médenine Nord", "Médenine Sud", "Sidi Makhlouf", "Zarzis"
  ],
  "Monastir": [
    "Bembla", "Beni Hassen", "Jammel", "Ksar Hellal",
    "Ksibet el-Médiouni", "Moknine", "Monastir", "Ouerdanine",
    "Sahline", "Sayada-Lamta-Bou Hajar", "Téboulba", "Zeramdine"
  ],
  "Nabeul": [
    "Beni Khalled", "Beni Khiar", "Bou Argoub",
    "Dar Chaabane-El Fehri", "El Haouaria", "El Mida", "Grombalia",
    "Hammam Ghezèze", "Hammamet", "Kélibia", "Korba",
    "Korbous", "Menzel Temime", "Nabeul", "Soliman", "Takelsa"
  ],
  "Sfax": [
    "Agareb", "Bir Ali Ben Khalifa", "Djebeniana", "El Amra",
    "El Hencha", "Ghraïba", "Kerkenah", "La Skhira", "Mahras",
    "Menzel Chaker", "Sakiet Eddaïer", "Sakiet Ezzit",
    "Sfax Est", "Sfax Médina", "Sfax Ouest", "Thyna", "Tina"
  ],
  "Sidi Bouzid": [
    "Ben Aoun", "Bir El Hafey", "Cebbala Ouled Asker", "Jelma",
    "Menzel Bouzaïene", "Mezzouna", "Ouled Haffouz",
    "Regueb", "Sidi Bouzid Est", "Sidi Bouzid Ouest", "Souk Jedid"
  ],
  "Siliana": [
    "Bargou", "Bouarada", "El Aroussa", "El Krib", "Gaâfour",
    "Kesra", "Makthar", "Rohia", "Sidi Bou Rouis",
    "Siliana Nord", "Siliana Sud"
  ],
  "Sousse": [
    "Akouda", "Bou Ficha", "Enfidha", "Hammam Sousse", "Hergla",
    "Kalaa Kebira", "Kalaa Seghira", "M'saken", "Sidi Bou Ali",
    "Sidi El Hani", "Sousse Jaouhara", "Sousse Médina",
    "Sousse Nord", "Sousse Riadh", "Zaouiet Sousse"
  ],
  "Tataouine": [
    "Bir Lahmar", "Dehiba", "Ghomrassen",
    "Remada", "Smar", "Tataouine Nord", "Tataouine Sud"
  ],
  "Tozeur": [
    "Degache", "El Hamma du Djerid", "Nefta", "Tameghza", "Tozeur"
  ],
  "Tunis": [
    "Bab Bhar", "Bab Souika", "Carthage", "El Hrairia", "El Kabbaria",
    "El Menzah", "El Omrane", "El Omrane Supérieur", "El Ouardia",
    "Ettahrir", "Ezzouhour", "Jebel Jelloud", "La Goulette",
    "La Marsa", "La Médina", "Le Bardo", "Le Kram",
    "Séjoumi", "Sidi El Béchir", "Sidi Hassine"
  ],
  "Zaghouan": [
    "Bir Mcherga", "El Fahs", "Nadhour",
    "Saouaf", "Zaghouan", "Zriba"
  ]
};
