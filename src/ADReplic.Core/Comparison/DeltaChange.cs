namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Représente un élément présent dans les deux audits comparés mais dont
    /// l'état a évolué entre les deux. Le couple Before/After permet à l'UI
    /// d'afficher la transition (par exemple "Healthy -> Failing") sans avoir
    /// à recalculer la différence ailleurs.
    /// </summary>
    /// <typeparam name="T">Type du modèle suivi (DC, lien, échec, etc.).</typeparam>
    public sealed class DeltaChange<T>
    {
        /// <summary>Clé d'identité stable utilisée pour matcher Before et After.</summary>
        public string Key { get; set; }

        /// <summary>Instance issue de l'audit de référence (avant).</summary>
        public T Before { get; set; }

        /// <summary>Instance issue de l'audit courant (après).</summary>
        public T After { get; set; }
    }
}
