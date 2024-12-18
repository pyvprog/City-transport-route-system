let map = L.map('map').setView([48.3794, 31.1656], 6);
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19
}).addTo(map);

let routesData = {};
let currentMarkers = [];
let specialMarkers = {
    startPoint: null,
    endPoint: null
};
let currentRouteControl = null;

const routesFileUrl = 'Data/routes.json';

const stopIcon = L.icon({
    iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon.png',
    iconSize: [20, 30],
    iconAnchor: [10, 30],
    popupAnchor: [0, -30]
});

const startPointIcon = L.icon({
    iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon.png',
    iconSize: [35, 45],
    iconAnchor: [17, 45],
    popupAnchor: [0, -45]
});

const endPointIcon = L.icon({
    iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon.png',
    iconSize: [35, 45],
    iconAnchor: [17, 45],
    popupAnchor: [0, -45]
});

fetch(routesFileUrl)
    .then(response => response.json())
    .then(data => {
        routesData = data;
        console.log('Дані маршрутів завантажено:', routesData);
    })
    .catch(error => console.error('Помилка завантаження JSON:', error));

function clearMarkersAndRoutes() {
    if (currentRouteControl) {
        map.removeControl(currentRouteControl);
        currentRouteControl = null;
    }

    currentMarkers.forEach(marker => map.removeLayer(marker));
    currentMarkers = [];

    console.log("Маршрути та маркери очищено.");
}


function moveMapToCity(data) {
    try {
        const parsedData = JSON.parse(data);
        map.setView([parsedData.lat, parsedData.lng], parsedData.zoom);
    } catch (error) {
        console.error("Помилка переміщення карти: ", error);
    }
}

function setMapCenter(lat, lng, zoom = 12) {
    try {
        map.setView([lat, lng], zoom);
        console.log(`Карта центрована на: Latitude = ${lat}, Longitude = ${lng}, Zoom = ${zoom}`);
    } catch (error) {
        console.error("Помилка центрування карти: ", error);
    }
}

//function findNearestStop(point) {
//    let nearestStop = null;
//    let minDistance = Infinity;

//    Object.values(routesData).forEach(cityRoutes => {
//        cityRoutes.forEach(route => {
//            route.Stops.forEach(stop => {
//                const distance = getDistance(point, [stop.Item2, stop.Item3]);
//                if (distance < minDistance) {
//                    minDistance = distance;
//                    nearestStop = { stop, route };
//                }
//            });
//        });
//    });

//    return nearestStop;
//}

function addMarker(lat, lng, markerKey) {
    let iconOptions;

    switch (markerKey) {
        case 'startPoint':
            iconOptions = startPointIcon;
            clearMarker(markerKey);
            specialMarkers[markerKey] = L.marker([lat, lng], { icon: iconOptions }).addTo(map);
            specialMarkers[markerKey].bindPopup(`Місце відправлення<br>Latitude: ${lat}, Longitude: ${lng}`).openPopup();
            break;

        case 'endPoint':
            iconOptions = endPointIcon;
            clearMarker(markerKey);
            specialMarkers[markerKey] = L.marker([lat, lng], { icon: iconOptions }).addTo(map);
            specialMarkers[markerKey].bindPopup(`Місце призначення<br>Latitude: ${lat}, Longitude: ${lng}`).openPopup();
            break;

        default:
            iconOptions = stopIcon;
            const marker = L.marker([lat, lng], { icon: iconOptions }).addTo(map);
            marker.bindPopup(`Latitude: ${lat}, Longitude: ${lng}`);
            currentMarkers.push(marker);
            break;
    }
}

function clearMarker(markerKey) {
    if (specialMarkers[markerKey]) {
        map.removeLayer(specialMarkers[markerKey]);
        specialMarkers[markerKey] = null;
    }
}

function getDistance(p1, p2) {
    const R = 6371;
    const dLat = (p2[0] - p1[0]) * Math.PI / 180;
    const dLon = (p2[1] - p1[1]) * Math.PI / 180;
    const a = Math.sin(dLat / 2) ** 2 +
        Math.cos(p1[0] * Math.PI / 180) * Math.cos(p2[0] * Math.PI / 180) *
        Math.sin(dLon / 2) ** 2;

    return R * (2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a)));
}

let currentRoutes = [];

function drawCityRoutes(city) {
    clearMarkersAndRoutes();
    clearRoutes();

    if (!routesData[city]) {
        console.warn(`Маршрути для міста "${city}" не знайдено.`);
        return;
    }

    console.log(`Побудова маршрутів для міста: ${city}`);

    routesData[city].forEach(route => {
        console.log(`Маршрут типу: ${route.TransportType || 'невідомий'}`);

        const lineColor = getRouteColor(route.TransportType);
        let routeWaypoints = [];

        route.Stops.forEach((stop, index) => {
            if (stop.Item2 && stop.Item3) {
                const marker = L.marker([stop.Item2, stop.Item3], { icon: stopIcon }) 
                    .addTo(map)
                    .bindPopup(`
                        <b>${stop.Item1}</b><br>
                        Опис маршруту: ${route.Description || 'Опис відсутній'}<br>
                        Тип транспорту: ${route.TransportType || 'невідомий'}
                    `);

                currentMarkers.push(marker); 
                routeWaypoints.push(L.latLng(stop.Item2, stop.Item3)); 
            }
        });

        if (routeWaypoints.length > 1) {
            const polyline = L.polyline(routeWaypoints, {
                color: lineColor,
                weight: 4,
                opacity: 0.8
            }).addTo(map);

            currentRoutes.push(polyline);
        }
    });

    console.log(`Маршрути успішно побудовані для міста: ${city}`);
}

function clearRoutes() {
    currentRoutes.forEach(route => map.removeLayer(route));
    currentRoutes = [];
}


function getRouteColor(type) {
    console.log(`Отримано тип транспорту: ${type}`);
    switch (type?.toLowerCase()) {
        case 'автобус': return 'red';      
        case 'тролейбус': return 'green';  
        case 'трамвай': return 'orange';  
        default: return 'blue';            
    }
}


//function drawRoute(startPoint, endPoint) {
//    clearMarkersAndRoutes();

//    if (!startPoint || !endPoint) {
//        console.error("Не вказано точки початку або кінця маршруту.");
//        return;
//    }

//    const waypoints = [
//        L.latLng(startPoint[0], startPoint[1]),
//        L.latLng(endPoint[0], endPoint[1])
//    ];

//    currentRouteControl = L.Routing.control({
//        waypoints: waypoints,
//        routeWhileDragging: true,
//        createMarker: (i, waypoint, n) => {
//            return L.marker(waypoint.latLng).bindPopup(i === 0 ? "Початок маршруту" : "Кінець маршруту");
//        }
//    }).addTo(map);

//    console.log("Маршрут побудовано між точками:", startPoint, endPoint);
//}


function receiveDataFromCSharp(data) {
    try {
        const parsedData = JSON.parse(data);

        clearMarkersAndRoutes();

        if (parsedData.city) {
            console.log(`Отримано місто з C#: ${parsedData.city}`);
            const city = parsedData.city;

            if (cityCoordinates[city]) {
                const { lat, lng } = cityCoordinates[city];
                setMapCenter(lat, lng, 12);
                drawCityRoutes(city);
            } else {
                console.warn(`Координати для міста "${city}" відсутні.`);
            }
        }

        if (parsedData.start && parsedData.end) {
            console.log("Отримано координати для маршруту:", parsedData.start, parsedData.end);
            drawRoute(parsedData.start, parsedData.end);
        }
    } catch (error) {
        console.error("Помилка обробки даних з C#: ", error);
    }
}


function isValidCoordinate(coord) {
    return Array.isArray(coord) && coord.length === 2 &&
        typeof coord[0] === "number" && typeof coord[1] === "number";
}

const cityCoordinates = {
    "Київ": { lat: 50.4501, lng: 30.5234 },
    "Львів": { lat: 49.8397, lng: 24.0297 },
    "Херсон": { lat: 46.6356, lng: 32.6164 }
};
