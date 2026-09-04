class AfkService {
    constructor() {
        this.afkUsers = new Map(); // key: `${guildId}-${userId}`, value: { reason, timestamp, originalNickname }
        this.mentionCooldowns = new Map(); // key: `${guildId}-${channelId}-${userId}`, value: timestamp
    }

    setAfk(guildId, userId, reason, originalNickname) {
        this.afkUsers.set(`${guildId}-${userId}`, {
            reason: reason || 'AFK',
            timestamp: Date.now(),
            originalNickname
        });
    }

    getAfk(guildId, userId) {
        return this.afkUsers.get(`${guildId}-${userId}`);
    }

    removeAfk(guildId, userId) {
        return this.afkUsers.delete(`${guildId}-${userId}`);
    }

    isOnCooldown(guildId, channelId, userId) {
        const key = `${guildId}-${channelId}-${userId}`;
        const expiration = this.mentionCooldowns.get(key);
        if (expiration && expiration > Date.now()) {
            return true;
        }
        this.mentionCooldowns.set(key, Date.now() + 8000); // 8 segundos de cooldown
        return false;
    }
}

export default new AfkService();
